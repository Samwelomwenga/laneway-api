namespace Laneway.Api;

public record UserDto
(
    Guid Id,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? Bio,
    string? Language,
    string TimeZone,
    string Location,
    Uri? ProfilePictureUrl,
    List<AccountDto> Accounts,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy
);

public record UserSummaryDto
(
    Guid Id,
    string Username,
    string FirstName,
    string LastName,
    Uri? ProfilePictureUrl,
    string? Bio
);

public record UpdateProfileDto
(
    string Username,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? Bio,
    string Language,
    string TimeZone,
    string Location,
    Uri? ProfilePictureUrl
);
