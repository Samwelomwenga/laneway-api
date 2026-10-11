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
