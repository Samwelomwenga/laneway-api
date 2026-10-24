using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Laneway.Api;

public interface IAuthService
{
    Task<ApiResponse<object?>> SignUpAsync(SignUpWrite write);
    Task<AuthReply<SignedInDto>> VerifyEmailAsync(string token, string? userAgent);
    Task<ApiResponse<object?>> ResendVerificationAsync(string email);
    Task<ApiResponse<object?>> ForgotPasswordAsync(string email);
    Task<AuthReply<bool>> ResetPasswordAsync(string token, string newPassword);
    Task<ApiResponse<UsernameAvailabilityDto>> UsernameAvailableAsync(string? username);
    Task<AuthReply<SignedInDto>> SignInAsync(SignInWrite write, string? userAgent);
    Task<AuthReply<SignedInDto>> RefreshAsync(string? presented);
    Task<AuthReply<bool>> SignOutAsync(Guid userId, Guid sessionId);
    Task<AuthReply<bool>> SignOutEverywhereAsync(Guid userId);
    Task<ApiResponse<List<SessionDto>>> ListSessionsAsync(Guid userId, Guid currentSessionId);
    Task<AuthReply<bool>> EndSessionAsync(Guid userId, Guid currentSessionId, Guid sessionId);
    Task<AuthReply<bool>> ChangePasswordAsync(Guid userId, Guid sessionId, ChangePasswordWrite write);
    Task<AuthReply<bool>> ChangeEmailAsync(Guid userId, Guid sessionId, ChangeEmailWrite write);
    Task<AuthReply<bool>> ConfirmEmailChangeAsync(string token, string? userAgent);
    Task<AuthReply<bool>> DeleteUserAsync(Guid userId, Guid sessionId, DeleteUserWrite write);
}

public sealed class AuthService(
    ApplicationDbContext database,
    Passwords passwords,
    PasswordPolicy passwordPolicy,
    AccessTokens tokens,
    AuthMeter meter,
    Messages messages,
    TimeProvider time,
    ILoggerFactory loggers) : IAuthService
{
    private const string IncorrectCredentials = "Email or password is incorrect.";

    private const string CheckYourEmail = "Check your email to finish signing up.";

    private const string CheckYourEmailForReset = "If an account uses this email, we sent it a reset link.";

    private const string ConfirmEmailFirst = "Confirm your email before signing in. Check your inbox for the link.";

    private const string RefreshedElsewhere = "Another request refreshed this session. Retry with its cookie.";

    private const string TooManyWrongPasswords = "Too many wrong passwords. Try again after the Retry-After delay.";

    private readonly ILogger _log = loggers.CreateLogger(AuthLog.Category);

    public async Task<ApiResponse<object?>> SignUpAsync(SignUpWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var fields = write.User;
        if (await UsernameTakenAsync(fields.Username))
        {
            return UsernameTaken();
        }

        if (await passwordPolicy.CheckAsync(write.Password, fields.Username, fields.Email) is { } refused)
        {
            return ApiResponse<object?>.ErrorResponse("Invalid data", StatusCodes.Status400BadRequest, [refused]);
        }

        var passwordHash = passwords.Hash(write.Password);
        var now = time.GetUtcNow().UtcDateTime;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction = await database.Database.BeginTransactionAsync();
                var outcome = await SignUpUserAsync(fields, passwordHash, now);
                await database.SaveChangesAsync();
                await transaction.CommitAsync();

                LogVerification(outcome);
                return CheckEmail();
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                database.ChangeTracker.Clear();
                if (await UsernameTakenAsync(fields.Username))
                {
                    return UsernameTaken();
                }

                if (attempt > 1 || !await database.Users.AnyAsync(u => u.Email == fields.Email))
                {
                    throw;
                }
            }
        }
    }

    private async Task<Verification> SignUpUserAsync(NewUser fields, string passwordHash, DateTime now)
    {
        var heldBy = await database.Users
            .Where(u => u.Email == fields.Email)
            .Select(u => (Guid?)u.Id)
            .SingleOrDefaultAsync();
        if (heldBy is null)
        {
            var user = UserRules.Create(fields, passwordHash, now);
            database.Users.Add(user);
            return await IssueVerificationAsync(user.Id, fields.Email, now);
        }

        await database.TryLockUserAsync(heldBy.Value);
        var held = await database.Users.SingleOrDefaultAsync(u => u.Id == heldBy);
        if (held is null)
        {
            return Verification.Nothing;
        }

        if (held.Verified)
        {
            return Verification.Skip(held.Id, MessageSkip.Verified);
        }

        if (await AddressCap.ReachedAsync(database, held.Id, now))
        {
            return Verification.Skip(held.Id, MessageSkip.AddressCapped);
        }

        held.Replace(fields, passwordHash, now);
        await DeleteVerificationAsync(held.Id);
        return await IssueVerificationAsync(held.Id, held.Email, now);
    }

    public async Task<ApiResponse<object?>> ResendVerificationAsync(string email)
    {
        ArgumentNullException.ThrowIfNull(email);

        var now = time.GetUtcNow().UtcDateTime;
        await using var transaction = await database.Database.BeginTransactionAsync();
        var outcome = await ResendToAsync(email, now);
        await database.SaveChangesAsync();
        await transaction.CommitAsync();

        LogVerification(outcome);
        return CheckEmail();
    }

    private async Task<Verification> ResendToAsync(string email, DateTime now)
    {
        var userId = await database.Users.Where(u => u.Email == email).Select(u => (Guid?)u.Id).SingleOrDefaultAsync();
        if (userId is not { } id || !await database.TryLockUserAsync(id))
        {
            return Verification.Skip(null, MessageSkip.UnknownAccount);
        }

        if (await database.Users.Where(u => u.Id == id).Select(u => u.Verified).SingleAsync())
        {
            return Verification.Skip(id, MessageSkip.Verified);
        }

        if (await AddressCap.ReachedAsync(database, id, now))
        {
            return Verification.Skip(id, MessageSkip.AddressCapped);
        }

        if (await SuppressedAsync(email, MessageType.VerifyEmail))
        {
            return Verification.Skip(id, MessageSkip.Suppressed);
        }

        await DeleteVerificationAsync(id);
        AddVerification(id, now);
        return new Verification(id, SignedUp: false, Skipped: null);
    }

    public async Task<ApiResponse<object?>> ForgotPasswordAsync(string email)
    {
        ArgumentNullException.ThrowIfNull(email);

        var now = time.GetUtcNow().UtcDateTime;
        await using var transaction = await database.Database.BeginTransactionAsync();
        var (userId, skipped) = await SendResetAsync(email, now);
        await database.SaveChangesAsync();
        await transaction.CommitAsync();

        if (skipped is not null)
        {
            _log.MessageSkipped(MessageType.ResetPassword, skipped, userId);
        }

        return ApiResponse<object?>.SuccessResponse(null, CheckYourEmailForReset, StatusCodes.Status202Accepted);
    }

    private async Task<(Guid? UserId, string? Skipped)> SendResetAsync(string email, DateTime now)
    {
        var userId = await database.Users.Where(u => u.Email == email).Select(u => (Guid?)u.Id).SingleOrDefaultAsync();
        if (userId is not { } id || !await database.TryLockUserAsync(id))
        {
            return (null, MessageSkip.UnknownAccount);
        }

        var user = await database.Users.AsNoTracking().SingleAsync(u => u.Id == id);
        if (!user.Verified)
        {
            return (id, MessageSkip.Unverified);
        }

        if (await AddressCap.ReachedAsync(database, id, now))
        {
            return (id, MessageSkip.AddressCapped);
        }

        if (await SuppressedAsync(email, MessageType.ResetPassword))
        {
            return (id, MessageSkip.Suppressed);
        }

        await DeleteTokensAsync(id, AuthTokenKind.Reset);
        var (row, value) = AuthToken.Issue(id, AuthTokenKind.Reset, AuthToken.ResetLifetime, now);
        database.AuthTokens.Add(row);
        messages.Add(new ResetPassword(id, user.Username) { Token = value });
        return (id, null);
    }

    public async Task<AuthReply<bool>> ResetPasswordAsync(string token, string newPassword)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(newPassword);

        var hash = AuthToken.Hash(token);
        var now = time.GetUtcNow().UtcDateTime;
        var user = await database.AuthTokens
            .Where(t => t.TokenHash == hash && t.Kind == AuthTokenKind.Reset && t.ExpiresAt > now)
            .Join(database.Users, t => t.UserId, u => u.Id, (_, u) => u)
            .AsNoTracking()
            .SingleOrDefaultAsync();
        if (user is null)
        {
            return InvalidLink();
        }

        if (Passwords.Length(newPassword) < Passwords.MinimumLength)
        {
            return Invalid(new ApiError(
                "newPassword",
                ErrorCodes.TooShort,
                $"'newPassword' must be at least {Passwords.MinimumLength} characters."));
        }

        if (await passwordPolicy.CheckAsync(newPassword, user.Username, user.Email, "newPassword") is { } refused)
        {
            return Invalid(refused);
        }

        var newHash = passwords.Hash(newPassword);
        await using var transaction = await database.Database.BeginTransactionAsync();
        var used = await database.TryLockUserAsync(user.Id)
            && await database.AuthTokens.Where(t => t.TokenHash == hash && t.ExpiresAt > now).ExecuteDeleteAsync() == 1;
        if (!used)
        {
            return InvalidLink();
        }

        await ReplacePasswordAsync(user.Id, newHash, now);
        await database.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(u => u
            .SetProperty(e => e.FailedSignInCount, 0)
            .SetProperty(e => e.LastFailedSignInAt, (DateTime?)null)
            .SetProperty(e => e.SignInBlockedUntil, (DateTime?)null));
        await DeleteTokensAsync(user.Id, AuthTokenKind.ChangeEmail);
        var ended = await EndAllAsync(user.Id, keep: null, SessionEndReason.PasswordReset, now);
        messages.Add(new PasswordChanged(user.Id, PasswordChangeHow.Reset, now, UserAgent: null, user.TimeZone));
        await database.SaveChangesAsync();
        await transaction.CommitAsync();

        _log.PasswordReset(user.Id);
        LogEnded(user.Id, ended, SessionEndReason.PasswordReset);
        return NoContent("Password reset", RefreshCookieChange.None);
    }

    private async Task ReplacePasswordAsync(Guid userId, string newHash, DateTime now)
    {
        var replaced = await database.Users.Where(u => u.Id == userId).Select(u => u.PasswordHash).SingleAsync();
        database.PasswordHistory.Add(PasswordHistoryEntry.Of(userId, replaced, now));
        await database.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u
            .SetProperty(e => e.PasswordHash, newHash)
            .SetProperty(e => e.UpdatedAt, now)
            .SetProperty(e => e.UpdatedBy, userId));
    }

    private static AuthReply<bool> InvalidLink() =>
        Invalid(new ApiError("token", ErrorCodes.InvalidToken, "This link is invalid or has expired."));

    private async Task<Verification> IssueVerificationAsync(Guid userId, string email, DateTime now)
    {
        if (await SuppressedAsync(email, MessageType.VerifyEmail))
        {
            return new Verification(userId, SignedUp: true, MessageSkip.Suppressed);
        }

        AddVerification(userId, now);
        return new Verification(userId, SignedUp: true, Skipped: null);
    }

    private async Task<bool> SuppressedAsync(string email, MessageType type) =>
        await Suppressions.StoppingAsync(database, email, MessageRoutes.For(type), CancellationToken.None) is not null;

    private void LogVerification(Verification outcome)
    {
        if (outcome is { SignedUp: true, UserId: { } signedUp })
        {
            _log.SignUp(signedUp);
        }

        if (outcome.Skipped is { } reason)
        {
            _log.MessageSkipped(MessageType.VerifyEmail, reason, outcome.UserId);
        }
    }

    private sealed record Verification(Guid? UserId, bool SignedUp, string? Skipped)
    {
        public static Verification Nothing { get; } = new(null, SignedUp: false, Skipped: null);

        public static Verification Skip(Guid? userId, string reason) => new(userId, SignedUp: false, reason);
    }

    private static ApiResponse<object?> CheckEmail() =>
        ApiResponse<object?>.SuccessResponse(null, CheckYourEmail, StatusCodes.Status202Accepted);

    private Task DeleteVerificationAsync(Guid userId) => DeleteTokensAsync(userId, AuthTokenKind.Verification);

    private Task DeleteTokensAsync(Guid userId, AuthTokenKind kind) =>
        database.AuthTokens.Where(t => t.UserId == userId && t.Kind == kind).ExecuteDeleteAsync();

    private void AddVerification(Guid userId, DateTime now)
    {
        var (row, value) = AuthToken.Issue(userId, AuthTokenKind.Verification, AuthToken.VerificationLifetime, now);
        database.AuthTokens.Add(row);
        messages.Add(new VerifyEmail(userId) { Token = value });
    }

    private Task<bool> UsernameTakenAsync(string username) =>
        database.Users.AnyAsync(u => u.Username == username);

    private static ApiResponse<object?> UsernameTaken() =>
        UserRules.AlreadyExists<object?>(
            [new ApiError("username", ErrorCodes.Duplicate, "A user with this username already exists.")]);

    public async Task<AuthReply<SignedInDto>> VerifyEmailAsync(string token, string? userAgent)
    {
        ArgumentNullException.ThrowIfNull(token);

        var hash = AuthToken.Hash(token);
        var now = time.GetUtcNow().UtcDateTime;
        var userId = await database.AuthTokens
            .Where(t => t.TokenHash == hash && t.Kind == AuthTokenKind.Verification && t.ExpiresAt > now)
            .Select(t => (Guid?)t.UserId)
            .SingleOrDefaultAsync();
        if (userId is not { } id)
        {
            return InvalidToken();
        }

        await using var transaction = await database.Database.BeginTransactionAsync();
        var used = await database.TryLockUserAsync(id)
            && await database.AuthTokens.Where(t => t.TokenHash == hash && t.ExpiresAt > now).ExecuteDeleteAsync() == 1;
        if (!used)
        {
            return InvalidToken();
        }

        await database.Users.Where(u => u.Id == id).ExecuteUpdateAsync(u => u
            .SetProperty(e => e.Verified, true)
            .SetProperty(e => e.FailedSignInCount, 0)
            .SetProperty(e => e.LastFailedSignInAt, (DateTime?)null)
            .SetProperty(e => e.SignInBlockedUntil, (DateTime?)null)
            .SetProperty(e => e.UpdatedAt, now)
            .SetProperty(e => e.UpdatedBy, id));
        var session = database.Sessions.Add(Session.Start(id, userAgent, now)).Entity;
        var (cookie, _) = AddRefreshToken(session, now);
        await database.SaveChangesAsync();
        await transaction.CommitAsync();

        _log.SignInSucceeded(id, session.Id);
        meter.SignInAttempted(SignInResult.Succeeded);
        return SignedIn(session, cookie, "Email verified");
    }

    private static AuthReply<SignedInDto> InvalidToken() =>
        AuthReply.Of(ApiResponse<SignedInDto>.ErrorResponse(
            "Invalid data",
            StatusCodes.Status400BadRequest,
            [new ApiError("token", ErrorCodes.InvalidToken, "This link is invalid or has expired.")]));

    public async Task<ApiResponse<UsernameAvailabilityDto>> UsernameAvailableAsync(string? username)
    {
        var name = Usernames.Normalize(username);
        var available = name is not null
            && Usernames.IsValid(name)
            && !await database.Users.AnyAsync(user => user.Username == name);
        return ApiResponse<UsernameAvailabilityDto>.SuccessResponse(
            new UsernameAvailabilityDto(available), "Username checked", StatusCodes.Status200OK);
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
            return await CountFailureAsync(user, now) is null
                ? Rejected(user.Id, SignInResult.WrongPassword)
                : Rejected(user.Id, SignInResult.Blocked);
        }

        if (!user.Verified)
        {
            _log.SignInFailed(user.Id, blocked: false);
            meter.SignInAttempted(SignInResult.Unverified);
            return AuthReply.Of(ApiResponse<SignedInDto>.ErrorResponse(
                ConfirmEmailFirst,
                StatusCodes.Status403Forbidden,
                [new ApiError(null, ErrorCodes.EmailNotVerified, ConfirmEmailFirst)]));
        }

        await using var transaction = await database.Database.BeginTransactionAsync();
        if (!await ClearFailuresAsync(user, check, now))
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
        return NoContent("Signed out", RefreshCookieChange.Cleared);
    }

    public async Task<AuthReply<bool>> SignOutEverywhereAsync(Guid userId)
    {
        var ended = await EndAllAsync(
            userId, keep: null, SessionEndReason.SignedOutEverywhere, time.GetUtcNow().UtcDateTime);
        LogEnded(userId, ended, SessionEndReason.SignedOutEverywhere);
        return NoContent("Signed out everywhere", RefreshCookieChange.Cleared);
    }

    public Task<AuthReply<bool>> ChangePasswordAsync(Guid userId, Guid sessionId, ChangePasswordWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        return WithPasswordAsync(userId, sessionId, write.CurrentPassword, PasswordCheckedEndpoint.ChangePassword, async (user, now) =>
        {
            await ReplacePasswordAsync(userId, passwords.Hash(write.NewPassword), now);
            var ended = await EndAllAsync(userId, keep: sessionId, SessionEndReason.PasswordChanged, now);
            var userAgent = await database.Sessions.Where(s => s.Id == sessionId).Select(s => s.UserAgent).SingleAsync();
            messages.Add(new PasswordChanged(userId, PasswordChangeHow.Changed, now, userAgent, user.TimeZone));
            await database.SaveChangesAsync();

            _log.PasswordChanged(userId, sessionId);
            LogEnded(userId, ended, SessionEndReason.PasswordChanged);
            return NoContent("Password changed", RefreshCookieChange.None);
        },
        refuseFirst: async user =>
        {
            if (write.NewPassword == write.CurrentPassword)
            {
                return Invalid(new ApiError(
                    "newPassword", ErrorCodes.SameAsCurrent, "'newPassword' must differ from your current password."));
            }

            return await passwordPolicy.CheckAsync(write.NewPassword, user.Username, user.Email, "newPassword") is { } refused
                ? Invalid(refused)
                : null;
        });
    }

    public Task<AuthReply<bool>> ChangeEmailAsync(Guid userId, Guid sessionId, ChangeEmailWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        return WithPasswordAsync(userId, sessionId, write.Password, PasswordCheckedEndpoint.ChangeEmail, async (user, now) =>
        {
            var newEmail = write.NewEmail;
            if (await VerifiedOwnerExistsAsync(userId, newEmail))
            {
                return EmailTaken("newEmail");
            }

            await database.TryLockUserAsync(userId);
            if (newEmail == user.Email)
            {
                await DeleteTokensAsync(userId, AuthTokenKind.ChangeEmail);
                return AuthReply.Of(ApiResponse<bool>.SuccessResponse(
                    true, "This is already your email. Any pending change is cancelled.", StatusCodes.Status202Accepted));
            }

            if (await ConfirmEmailSkipAsync(newEmail, now) is { } skipped)
            {
                _log.MessageSkipped(MessageType.ConfirmEmailChange, skipped, userId);
                return CheckNewEmail();
            }

            await DeleteTokensAsync(userId, AuthTokenKind.ChangeEmail);
            var (row, value) = AuthToken.Issue(
                userId, AuthTokenKind.ChangeEmail, AuthToken.ChangeEmailLifetime, now, newEmail);
            database.AuthTokens.Add(row);
            messages.Add(new ConfirmEmailChange(user.Username, newEmail) { Token = value });
            await database.SaveChangesAsync();
            return CheckNewEmail();
        });
    }

    private async Task<string?> ConfirmEmailSkipAsync(string newEmail, DateTime now)
    {
        if (await AddressCap.NewAddressReachedAsync(database, newEmail, now))
        {
            return MessageSkip.AddressCapped;
        }

        return await SuppressedAsync(newEmail, MessageType.ConfirmEmailChange) ? MessageSkip.Suppressed : null;
    }

    public async Task<AuthReply<bool>> ConfirmEmailChangeAsync(string token, string? userAgent)
    {
        ArgumentNullException.ThrowIfNull(token);

        var hash = AuthToken.Hash(token);
        var now = time.GetUtcNow().UtcDateTime;
        var change = await database.AuthTokens
            .Where(t => t.TokenHash == hash && t.Kind == AuthTokenKind.ChangeEmail && t.ExpiresAt > now)
            .Select(t => new { t.UserId, NewEmail = t.Email! })
            .SingleOrDefaultAsync();
        if (change is null)
        {
            return InvalidLink();
        }

        var (userId, newEmail) = (change.UserId, change.NewEmail);
        await using var transaction = await database.Database.BeginTransactionAsync();
        if (!await database.TryLockUserAsync(userId))
        {
            return InvalidLink();
        }

        if (await VerifiedOwnerExistsAsync(userId, newEmail))
        {
            return EmailTaken(field: null);
        }

        if (await database.AuthTokens.Where(t => t.TokenHash == hash && t.ExpiresAt > now).ExecuteDeleteAsync() != 1)
        {
            return InvalidLink();
        }

        var user = await database.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        await database.Users.Where(u => u.Id != userId && u.Email == newEmail && !u.Verified).ExecuteDeleteAsync();
        try
        {
            await database.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u
                .SetProperty(e => e.Email, newEmail)
                .SetProperty(e => e.UpdatedAt, now)
                .SetProperty(e => e.UpdatedBy, userId));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return EmailTaken(field: null);
        }
        await DeleteTokensAsync(userId, AuthTokenKind.Reset);
        var (undo, value) = AuthToken.Issue(userId, AuthTokenKind.Undo, AuthToken.UndoLifetime, now, user.Email);
        database.AuthTokens.Add(undo);
        messages.Add(new EmailChanged(user.Email, newEmail, now, userAgent, user.TimeZone) { Token = value });
        await database.SaveChangesAsync();
        await transaction.CommitAsync();

        _log.EmailChanged(userId);
        return NoContent("Email changed", RefreshCookieChange.None);
    }

    private Task<bool> VerifiedOwnerExistsAsync(Guid userId, string email) =>
        database.Users.AnyAsync(u => u.Id != userId && u.Email == email && u.Verified);

    private static AuthReply<bool> EmailTaken(string? field) =>
        AuthReply.Of(UserRules.AlreadyExists<bool>(
            [new ApiError(field, ErrorCodes.Duplicate, "A user with this email already exists.")]));

    private static AuthReply<bool> CheckNewEmail() =>
        AuthReply.Of(ApiResponse<bool>.SuccessResponse(
            true, "Check your new email for a link to confirm it.", StatusCodes.Status202Accepted));

    public Task<AuthReply<bool>> DeleteUserAsync(Guid userId, Guid sessionId, DeleteUserWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        return WithPasswordAsync(userId, sessionId, write.Password, PasswordCheckedEndpoint.DeleteUser, async (_, now) =>
        {
            var soleAdmin = await database.SoleAdminPlacesAsync(userId);
            if (soleAdmin.Count > 0)
            {
                return AuthReply.Of(MemberLimits.Conflict<bool>(soleAdmin));
            }

            await database.WorkspaceMembers.Where(m => m.UserId == userId).ExecuteDeleteAsync();
            await database.BoardMembers.Where(m => m.UserId == userId).ExecuteDeleteAsync();
            await database.AuthTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            await database.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u
                .SetProperty(e => e.DeletedAt, now)
                .SetProperty(e => e.UpdatedAt, now)
                .SetProperty(e => e.UpdatedBy, userId));
            var ended = await EndAllAsync(userId, keep: null, SessionEndReason.UserDeleted, now);

            _log.UserDeleted(userId, sessionId);
            LogEnded(userId, ended, SessionEndReason.UserDeleted);
            return NoContent("User deleted", RefreshCookieChange.Cleared);
        });
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
        return NoContent(
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

    private async Task<List<Guid>> EndAllAsync(Guid userId, Guid? keep, SessionEndReason reason, DateTime now)
    {
        await database.Sessions
            .Where(Session.ActiveAt(now))
            .Where(s => s.UserId == userId && s.Id != keep)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.EndedAt, now)
                .SetProperty(e => e.EndReason, reason));
        return await database.Sessions
            .Where(s => s.UserId == userId && s.EndedAt == now && s.EndReason == reason)
            .Select(s => s.Id)
            .ToListAsync();
    }

    private void LogEnded(Guid userId, List<Guid> sessionIds, SessionEndReason reason)
    {
        foreach (var sessionId in sessionIds)
        {
            _log.SessionEnded(userId, sessionId, reason);
        }
    }

    private async Task<AuthReply<bool>> WithPasswordAsync(
        Guid userId,
        Guid sessionId,
        string password,
        PasswordCheckedEndpoint endpoint,
        Func<User, DateTime, Task<AuthReply<bool>>> action,
        Func<User, Task<AuthReply<bool>?>>? refuseFirst = null)
    {
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId);
        if (user is null)
        {
            return AuthReply.Of(NotAuthenticated.Response<bool>());
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (user.SignInBlockedUntil is { } runningBlock && SignInBlock.IsBlocked(user, now))
        {
            return PasswordCheckBlocked(userId, sessionId, endpoint, runningBlock - now);
        }

        var check = passwords.Verify(user, password);
        if (!check.Matched)
        {
            return await CountFailureAsync(user, now) is { } blockedUntil
                ? PasswordCheckBlocked(userId, sessionId, endpoint, blockedUntil - now)
                : PasswordCheckWrong(userId, sessionId, endpoint);
        }

        if (refuseFirst is not null && await refuseFirst(user) is { } refused)
        {
            return await ClearFailuresAsync(user, check, now)
                ? refused
                : await ClearLostAsync(userId, sessionId, endpoint, now);
        }

        await using var transaction = await database.Database.BeginTransactionAsync();
        if (!await ClearFailuresAsync(user, check, now))
        {
            return await ClearLostAsync(userId, sessionId, endpoint, now);
        }

        var reply = await action(user, now);
        await transaction.CommitAsync();
        return reply;
    }

    private async Task<AuthReply<bool>> ClearLostAsync(
        Guid userId, Guid sessionId, PasswordCheckedEndpoint endpoint, DateTime now)
    {
        var blockedUntil = await database.Users
            .Where(u => u.Id == userId)
            .Select(u => u.SignInBlockedUntil)
            .SingleOrDefaultAsync();
        return blockedUntil > now
            ? PasswordCheckBlocked(userId, sessionId, endpoint, blockedUntil.Value - now)
            : PasswordCheckWrong(userId, sessionId, endpoint);
    }

    private AuthReply<bool> PasswordCheckWrong(Guid userId, Guid sessionId, PasswordCheckedEndpoint endpoint)
    {
        _log.PasswordCheckFailed(userId, sessionId, blocked: false);
        meter.PasswordCheckFailed(endpoint, blocked: false);
        var field = endpoint == PasswordCheckedEndpoint.ChangePassword ? "currentPassword" : "password";
        return Invalid(new ApiError(field, ErrorCodes.WrongPassword, $"'{field}' is wrong."));
    }

    private AuthReply<bool> PasswordCheckBlocked(
        Guid userId, Guid sessionId, PasswordCheckedEndpoint endpoint, TimeSpan retryAfter)
    {
        _log.PasswordCheckFailed(userId, sessionId, blocked: true);
        meter.PasswordCheckFailed(endpoint, blocked: true);
        return new AuthReply<bool>(
            ApiResponse<bool>.ErrorResponse(
                TooManyWrongPasswords,
                StatusCodes.Status429TooManyRequests,
                [new ApiError(null, ErrorCodes.TooManyRequests, TooManyWrongPasswords)]),
            RefreshCookieChange.None,
            retryAfter);
    }

    private static AuthReply<bool> Invalid(ApiError error) =>
        AuthReply.Of(ApiResponse<bool>.ErrorResponse("Invalid data", StatusCodes.Status400BadRequest, [error]));

    private static ApiResponse<bool> SessionNotFound(Guid sessionId)
    {
        var message = $"Session {sessionId} does not exist.";
        return ApiResponse<bool>.ErrorResponse(
            message, StatusCodes.Status404NotFound, [new ApiError(null, ErrorCodes.NotFound, message)]);
    }

    private static AuthReply<bool> NoContent(string message, RefreshCookieChange cookie) =>
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

    private async Task<DateTime?> CountFailureAsync(User user, DateTime now)
    {
        while (true)
        {
            if (SignInBlock.IsBlocked(user, now))
            {
                return user.SignInBlockedUntil;
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

                return null;
            }

            if (await database.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId) is not { } reread)
            {
                return null;
            }

            user = reread;
        }
    }

    private async Task<bool> ClearFailuresAsync(User user, PasswordCheck check, DateTime now)
    {
        var (userId, checkedHash) = (user.Id, user.PasswordHash);
        var cleared = await database.Users
            .Where(u => u.Id == userId
                && u.PasswordHash == checkedHash
                && (u.SignInBlockedUntil == null || u.SignInBlockedUntil <= now))
            .ExecuteUpdateAsync(u => u
                .SetProperty(e => e.PasswordHash, check.HashToStore)
                .SetProperty(e => e.FailedSignInCount, 0)
                .SetProperty(e => e.LastFailedSignInAt, (DateTime?)null)
                .SetProperty(e => e.SignInBlockedUntil, (DateTime?)null));
        return cleared == 1;
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
