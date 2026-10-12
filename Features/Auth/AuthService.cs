using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public interface IAuthService
{
    Task<AuthReply<SignedUpDto>> SignUpAsync(SignUpWrite write, string? userAgent);
    Task<AuthReply<SignedInDto>> SignInAsync(SignInWrite write, string? userAgent);
    Task<AuthReply<SignedInDto>> RefreshAsync(string? presented);
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

        var user = await database.Users.FirstOrDefaultAsync(u => u.Email == write.Email);
        if (user is null)
        {
            passwords.VerifyDummy(write.Password);
            return Rejected(userId: null, SignInResult.UnknownAccount);
        }

        if (!passwords.Verify(user, write.Password))
        {
            return Rejected(user.Id, SignInResult.WrongPassword);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var session = database.Sessions.Add(Session.Start(user.Id, userAgent, now)).Entity;
        var (cookie, _) = AddRefreshToken(session, now);
        await database.SaveChangesAsync();

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
            .FromSql($"SELECT * FROM \"Sessions\" WHERE \"Id\" = {session.Id} FOR UPDATE")
            .Where(Session.ActiveAt(now))
            .AnyAsync();
        if (!stillActive)
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

        session.LastUsedAt = now;
        var (cookie, successor) = AddRefreshToken(session, now);
        await database.SaveChangesAsync();
        await transaction.CommitAsync();

        _log.SessionRefreshed(session.UserId, session.Id, successor.Id);
        meter.Refreshed(RefreshResult.Rotated);
        return SignedIn(session, cookie, "Refreshed");
    }

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
        var ended = await database.Sessions
            .Where(s => s.Id == session.Id && s.EndedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.EndedAt, now)
                .SetProperty(e => e.EndReason, SessionEndReason.TokenReused));
        if (ended == 1)
        {
            _log.SessionEnded(session.UserId, session.Id, SessionEndReason.TokenReused);
        }

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

    private AuthReply<SignedInDto> Rejected(Guid? userId, SignInResult result)
    {
        _log.SignInFailed(userId);
        meter.SignInAttempted(result);
        return AuthReply.Of(ApiResponse<SignedInDto>.ErrorResponse(
            IncorrectCredentials,
            401,
            [new ApiError(null, ErrorCodes.InvalidCredentials, IncorrectCredentials)]));
    }
}
