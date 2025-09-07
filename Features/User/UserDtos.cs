namespace DefaultNamespace;

public record UserDto
(
    Guid Id,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? Bio ,
    string? Language,
    string TimeZone,
    string Location,
    Uri? ProfilePictureUrl,
    bool IsActive,
    List<AccountDto> Accounts,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy
);

public record CreateUserDto
(
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? Bio ,
    string Language,
    string TimeZone,
    string Location,
    Uri? ProfilePictureUrl,
    bool IsActive
);

public record UpdateUserDto
(
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? Bio ,
    string Language,
    string TimeZone,
    string Location,
    Uri? ProfilePictureUrl,
    bool IsActive
);

public record UserSearchDto
(
    int PageNumber = 1,
    int PageSize = 10,
    string? SearchTerm = null,
    bool? IsActive = null
);
