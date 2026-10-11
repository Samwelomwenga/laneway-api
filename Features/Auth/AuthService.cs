using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed class AuthService(
    ApplicationDbContext database,
    Passwords passwords,
    AccessTokens tokens,
    AuthMeter meter,
    TimeProvider time,
    ILoggerFactory loggers)
{
    private const string IncorrectCredentials = "Email or password is incorrect.";

    private readonly ILogger _log = loggers.CreateLogger(AuthLog.Category);

    public async Task<ApiResponse<SignedUpDto>> SignUpAsync(SignUpWrite write, string? userAgent)
    {
        ArgumentNullException.ThrowIfNull(write);

        var taken = await UserRules.TakenAsync(database, write.User.Username, write.User.Email, exceptId: null);
        if (taken.Count > 0)
        {
            return UserRules.AlreadyExists<SignedUpDto>(taken);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var user = UserRules.Create(write.User, passwords.Hash(write.Password), now);
        database.Users.Add(user);
        var session = database.Sessions.Add(Session.Start(user.Id, userAgent, now)).Entity;
        await database.SaveChangesAsync();

        _log.SignUp(user.Id, session.Id);
        var token = tokens.Issue(user.Id, session.Id);
        return ApiResponse<SignedUpDto>.SuccessResponse(
            new SignedUpDto(token.Value, token.ExpiresAt, session.ExpiresAt, UserView.Of(user)),
            "Signed up",
            201);
    }

    public async Task<ApiResponse<SignedInDto>> SignInAsync(SignInWrite write, string? userAgent)
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

        var session = database.Sessions.Add(Session.Start(user.Id, userAgent, time.GetUtcNow().UtcDateTime)).Entity;
        await database.SaveChangesAsync();

        _log.SignInSucceeded(user.Id, session.Id);
        meter.SignInAttempted(SignInResult.Succeeded);
        var token = tokens.Issue(user.Id, session.Id);
        return ApiResponse<SignedInDto>.SuccessResponse(
            new SignedInDto(token.Value, token.ExpiresAt, session.ExpiresAt), "Signed in");
    }

    private ApiResponse<SignedInDto> Rejected(Guid? userId, SignInResult result)
    {
        _log.SignInFailed(userId);
        meter.SignInAttempted(result);
        return ApiResponse<SignedInDto>.ErrorResponse(
            IncorrectCredentials,
            401,
            [new ApiError(null, ErrorCodes.InvalidCredentials, IncorrectCredentials)]);
    }
}
