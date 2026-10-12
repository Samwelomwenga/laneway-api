using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public interface IAuthService
{
    Task<AuthReply<SignedUpDto>> SignUpAsync(SignUpWrite write, string? userAgent);
    Task<AuthReply<SignedInDto>> SignInAsync(SignInWrite write, string? userAgent);
    Task<AuthReply<SignedInDto>> RefreshAsync(string? presented);
    Task<AuthReply<bool>> SignOutAsync(Guid userId, Guid sessionId);
    Task<AuthReply<bool>> SignOutEverywhereAsync(Guid userId);
    Task<ApiResponse<List<SessionDto>>> ListSessionsAsync(Guid userId, Guid currentSessionId);
    Task<AuthReply<bool>> EndSessionAsync(Guid userId, Guid currentSessionId, Guid sessionId);
}

public sealed class AuthService(
    ApplicationDbContext database,
    Passwords passwords,
    PasswordPolicy passwordPolicy,
    AccessTokens tokens,
    AuthMeter meter,
    TimeProvider time,
    ILoggerFactory loggers) : IAuthService
{
    private const string IncorrectCredentials = "Email or password is incorrect.";

    private const string RefreshedElsewhere = "Another request refreshed this session. Retry with its cookie.";

    private readonly ILogger _log = loggers.CreateLogger(AuthLog.Category);

    public async Task<AuthReply<SignedUpDto>> SignUpAsync(SignUpWrite write, string? userAgent)
    {
        ArgumentNullException.ThrowIfNull(write);

        var taken = await UserRules.TakenAsync(database, write.User.Username, write.User.Email, exceptId: null);
        if (taken.Count > 0)
        {
            return AuthReply.Of(UserRules.AlreadyExists<SignedUpDto>(taken));
        }

        if (await passwordPolicy.CheckAsync(write.Password, write.User.Username, write.User.Email) is { } refused)
        {
            return AuthReply.Of(
                ApiResponse<SignedUpDto>.ErrorResponse("Invalid data", StatusCodes.Status400BadRequest, [refused]));
        }

        var now = time.GetUtcNow().UtcDateTime;
        var user = UserRules.Create(write.User, passwords.Hash(write.Password), now);
        database.Users.Add(user);
        var session = database.Sessions.Add(Session.Start(user.Id, userAgent, now)).Entity;
        var (cookie, _) = AddRefreshToken(session, now);
        await database.SaveChangesAsync();

        _log.SignUp(user.Id, session.Id);
        var token = tokens.Issue(user.Id, session.Id);
        return new AuthReply<SignedUpDto>(
            ApiResponse<SignedUpDto>.SuccessResponse(
                new SignedUpDto(token.Value, token.ExpiresAt, session.ExpiresAt, UserView.Of(user)),
                "Signed up",
                201),
            cookie);
    }

    public async Task<AuthReply<SignedInDto>> SignInAsync(SignInWrite write, string? userAgent)
    {
        ArgumentNullException.ThrowIfNull(write);

        var user = await database.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == write.Email);
        if (user is null)
        {
            passwords.VerifyDummy(write.Password);
            return Rejected(userId: null, SignInResult.UnknownAccount);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var check = passwords.Verify(user, write.Password);
        if (SignInBlock.IsBlocked(user, now))
        {
            return Rejected(user.Id, SignInResult.Blocked);
        }

        if (!check.Matched)
        {
            return await WrongPasswordAsync(user, now);
        }

        await using var transaction = await database.Database.BeginTransactionAsync();
        var checkedHash = user.PasswordHash;
        var cleared = await database.Users
            .Where(u => u.Id == user.Id
                && u.PasswordHash == checkedHash
                && (u.SignInBlockedUntil == null || u.SignInBlockedUntil <= now))
            .ExecuteUpdateAsync(u => u
                .SetProperty(e => e.PasswordHash, check.HashToStore)
                .SetProperty(e => e.FailedSignInCount, 0)
                .SetProperty(e => e.LastFailedSignInAt, (DateTime?)null)
                .SetProperty(e => e.SignInBlockedUntil, (DateTime?)null));
        if (cleared == 0)
        {
            return Rejected(user.Id, SignInResult.Blocked);
        }

        var session = database.Sessions.Add(Session.Start(user.Id, userAgent, now)).Entity;
        var (cookie, _) = AddRefreshToken(session, now);
        await database.SaveChangesAsync();
        await transaction.CommitAsync();

        _log.SignInSucceeded(user.Id, session.Id);
        meter.SignInAttempted(SignInResult.Succeeded);
        return SignedIn(session, cookie, "Signed in");
    }

    public async Task<AuthReply<SignedInDto>> RefreshAsync(string? presented)
    {
        if (presented is null)
        {
            return RefreshRejected(RefreshRejection.MissingCookie, sessionId: null, RefreshCookieChange.None);
        }

        var hash = RefreshToken.Hash(presented);
        var token = await database.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash);
        if (token is null)
        {
            return RefreshRejected(RefreshRejection.UnknownToken, sessionId: null, RefreshCookieChange.Cleared);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var session = await database.Sessions
            .Where(Session.ActiveAt(now))
            .SingleOrDefaultAsync(s => s.Id == token.SessionId && database.Users.Any(u => u.Id == s.UserId));
        if (session is null)
        {
            return RefreshRejected(RefreshRejection.SessionEnded, token.SessionId, RefreshCookieChange.Cleared);
        }

        if (token.RetiredAt is { } retiredAt)
        {
            return await RetiredAsync(token, session, retiredAt, now);
        }

        await using var transaction = await database.Database.BeginTransactionAsync();
        var stillActive = await database.Sessions
            .Where(s => s.Id == session.Id)
            .Where(Session.ActiveAt(now))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.LastUsedAt, now));
        if (stillActive == 0)
        {
            await transaction.RollbackAsync();
            return RefreshRejected(RefreshRejection.SessionEnded, session.Id, RefreshCookieChange.Cleared);
        }

        var retired = await database.RefreshTokens
            .Where(t => t.Id == token.Id && t.RetiredAt == null)
            .ExecuteUpdateAsync(t => t.SetProperty(r => r.RetiredAt, now));
        if (retired == 0)
        {
            await transaction.RollbackAsync();
            var winnerRetiredAt = await database.RefreshTokens
                .Where(t => t.Id == token.Id)
                .Select(t => t.RetiredAt)
                .SingleAsync();
            return await RetiredAsync(token, session, winnerRetiredAt ?? now, now);
        }

        var (cookie, successor) = AddRefreshToken(session, now);
        await database.SaveChangesAsync();
        await transaction.CommitAsync();

        _log.SessionRefreshed(session.UserId, session.Id, successor.Id);
        meter.Refreshed(RefreshResult.Rotated);
        return SignedIn(session, cookie, "Refreshed");
    }

    public async Task<AuthReply<bool>> SignOutAsync(Guid userId, Guid sessionId)
    {
        await EndAsync(userId, sessionId, SessionEndReason.SignedOut, time.GetUtcNow().UtcDateTime);
        return SessionsEnded("Signed out", RefreshCookieChange.Cleared);
    }

    public async Task<AuthReply<bool>> SignOutEverywhereAsync(Guid userId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await database.Sessions
            .Where(Session.ActiveAt(now))
            .Where(s => s.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.EndedAt, now)
                .SetProperty(e => e.EndReason, SessionEndReason.SignedOutEverywhere));
        var ended = await database.Sessions
            .Where(s => s.UserId == userId
                && s.EndedAt == now
                && s.EndReason == SessionEndReason.SignedOutEverywhere)
            .Select(s => s.Id)
            .ToListAsync();

        foreach (var sessionId in ended)
        {
            _log.SessionEnded(userId, sessionId, SessionEndReason.SignedOutEverywhere);
        }

        return SessionsEnded("Signed out everywhere", RefreshCookieChange.Cleared);
    }

    public async Task<ApiResponse<List<SessionDto>>> ListSessionsAsync(Guid userId, Guid currentSessionId)
    {
        var sessions = await database.Sessions
            .AsNoTracking()
            .Where(Session.ActiveAt(time.GetUtcNow().UtcDateTime))
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.LastUsedAt)
            .Select(s => new SessionDto(
                s.Id, s.CreatedAt, s.LastUsedAt, s.ExpiresAt, s.UserAgent, s.Id == currentSessionId))
            .ToListAsync();
        return ApiResponse<List<SessionDto>>.SuccessResponse(sessions, "Sessions retrieved");
    }

    public async Task<AuthReply<bool>> EndSessionAsync(Guid userId, Guid currentSessionId, Guid sessionId)
    {
        if (!await database.Sessions.AnyAsync(s => s.Id == sessionId && s.UserId == userId))
        {
            return AuthReply.Of(SessionNotFound(sessionId));
        }

        await EndAsync(userId, sessionId, SessionEndReason.EndedFromSessionList, time.GetUtcNow().UtcDateTime);
        return SessionsEnded(
            "Session ended",
            sessionId == currentSessionId ? RefreshCookieChange.Cleared : RefreshCookieChange.None);
    }

    private async Task EndAsync(Guid userId, Guid sessionId, SessionEndReason reason, DateTime now)
    {
        var ended = await database.Sessions
            .Where(Session.ActiveAt(now))
            .Where(s => s.Id == sessionId && s.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.EndedAt, now)
                .SetProperty(e => e.EndReason, reason));
        if (ended == 1)
        {
            _log.SessionEnded(userId, sessionId, reason);
        }
    }

    private static ApiResponse<bool> SessionNotFound(Guid sessionId)
    {
        var message = $"Session {sessionId} does not exist.";
        return ApiResponse<bool>.ErrorResponse(
            message, StatusCodes.Status404NotFound, [new ApiError(null, ErrorCodes.NotFound, message)]);
    }

    private static AuthReply<bool> SessionsEnded(string message, RefreshCookieChange cookie) =>
        new(ApiResponse<bool>.SuccessResponse(true, message, StatusCodes.Status204NoContent), cookie);

    private async Task<AuthReply<SignedInDto>> RetiredAsync(
        RefreshToken token, Session session, DateTime retiredAt, DateTime now)
    {
        if (now - retiredAt < RefreshToken.GraceWindow)
        {
            _log.RefreshSuperseded(session.UserId, session.Id);
            meter.Refreshed(RefreshResult.Superseded);
            return AuthReply.Of(ApiResponse<SignedInDto>.ErrorResponse(
                RefreshedElsewhere,
                StatusCodes.Status409Conflict,
                [new ApiError(null, ErrorCodes.RefreshSuperseded, RefreshedElsewhere)]));
        }

        _log.RefreshTokenReused(session.UserId, session.Id, token.Id);
        meter.Refreshed(RefreshResult.Reused);
        await EndAsync(session.UserId, session.Id, SessionEndReason.TokenReused, now);

        return new AuthReply<SignedInDto>(NotAuthenticated.Response<SignedInDto>(), RefreshCookieChange.Cleared);
    }

    private AuthReply<SignedInDto> RefreshRejected(string reason, Guid? sessionId, RefreshCookieChange cookie)
    {
        _log.RefreshRejected(reason, sessionId);
        meter.Refreshed(RefreshResult.Rejected);
        return new AuthReply<SignedInDto>(NotAuthenticated.Response<SignedInDto>(), cookie);
    }

    private (RefreshCookieChange Cookie, RefreshToken Row) AddRefreshToken(Session session, DateTime now)
    {
        var (row, value) = RefreshToken.Issue(session.Id, now);
        database.RefreshTokens.Add(row);
        return (RefreshCookieChange.To(value, session.ExpiresAt - now), row);
    }

    private AuthReply<SignedInDto> SignedIn(Session session, RefreshCookieChange cookie, string message)
    {
        var token = tokens.Issue(session.UserId, session.Id);
        return new AuthReply<SignedInDto>(
            ApiResponse<SignedInDto>.SuccessResponse(
                new SignedInDto(token.Value, token.ExpiresAt, session.ExpiresAt), message),
            cookie);
    }

    private async Task<AuthReply<SignedInDto>> WrongPasswordAsync(User user, DateTime now)
    {
        while (true)
        {
            if (SignInBlock.IsBlocked(user, now))
            {
                return Rejected(user.Id, SignInResult.Blocked);
            }

            var (userId, readCount, readLastFailedAt) = (user.Id, user.FailedSignInCount, user.LastFailedSignInAt);
            var blockStarted = SignInBlock.RecordFailure(user, now);
            var (count, lastFailedAt, blockedUntil) = (user.FailedSignInCount, user.LastFailedSignInAt, user.SignInBlockedUntil);
            var recorded = await database.Users
                .Where(u => u.Id == userId && u.FailedSignInCount == readCount && u.LastFailedSignInAt == readLastFailedAt)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(e => e.FailedSignInCount, count)
                    .SetProperty(e => e.LastFailedSignInAt, lastFailedAt)
                    .SetProperty(e => e.SignInBlockedUntil, blockedUntil));
            if (recorded == 1)
            {
                if (blockStarted)
                {
                    _log.SignInBlockStarted(userId, count, blockedUntil!.Value);
                    meter.SignInBlockStarted();
                }

                return Rejected(userId, SignInResult.WrongPassword);
            }

            if (await database.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId) is not { } reread)
            {
                return Rejected(userId, SignInResult.WrongPassword);
            }

            user = reread;
        }
    }

    private AuthReply<SignedInDto> Rejected(Guid? userId, SignInResult result)
    {
        _log.SignInFailed(userId, blocked: result == SignInResult.Blocked);
        meter.SignInAttempted(result);
        return AuthReply.Of(ApiResponse<SignedInDto>.ErrorResponse(
            IncorrectCredentials,
            401,
            [new ApiError(null, ErrorCodes.InvalidCredentials, IncorrectCredentials)]));
    }
}
