namespace Laneway.Api;

public record SignUpDto
(
    string? Username,
    string? Email,
    string? Password,
    string? FirstName,
    string? LastName,
    string? PhoneNumber,
    string? Bio,
    string? Language,
    string? TimeZone,
    string? Location,
    Uri? ProfilePictureUrl
)
{
    public string? Username { get; init; } = Usernames.Normalize(Username);
    public string? Email { get; init; } = Emails.Normalize(Email);
    public string? Password { get; init; } = Passwords.Normalize(Password);
}

public record SignInDto
(
    string? Email,
    string? Password
)
{
    public string? Email { get; init; } = Emails.Normalize(Email);
    public string? Password { get; init; } = Passwords.Normalize(Password);
}

public record ChangePasswordDto
(
    string? CurrentPassword,
    string? NewPassword
)
{
    public string? CurrentPassword { get; init; } = Passwords.Normalize(CurrentPassword);
    public string? NewPassword { get; init; } = Passwords.Normalize(NewPassword);
}

public record ChangeEmailDto
(
    string? Password,
    string? NewEmail
)
{
    public string? Password { get; init; } = Passwords.Normalize(Password);
    public string? NewEmail { get; init; } = Emails.Normalize(NewEmail);
}

public record DeleteUserDto
(
    string? Password
)
{
    public string? Password { get; init; } = Passwords.Normalize(Password);
}

public record SignedInDto
(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    DateTime SessionExpiresAt
);

public record SignedUpDto
(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    DateTime SessionExpiresAt,
    UserDto User
) : SignedInDto(AccessToken, AccessTokenExpiresAt, SessionExpiresAt);

public record UsernameCheckDto(string? Username);

public record UsernameAvailabilityDto(bool Available);

public record SessionDto
(
    Guid Id,
    DateTime CreatedAt,
    DateTime LastUsedAt,
    DateTime ExpiresAt,
    string UserAgent,
    bool Current
);
