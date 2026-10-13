namespace Laneway.Api;

public sealed record NewUser(
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? Bio,
    string Language,
    string TimeZone,
    string Location,
    Uri? ProfilePictureUrl);
