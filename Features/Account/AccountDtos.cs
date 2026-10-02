namespace Laneway.Api;

public record AccountDto(
    Guid Id,
    string Name,
    Guid CreatedBy,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid? UpdatedBy,
    UserDto User
);

public record CreateAccountDto(
    string Name,
    Guid UserId
);

public record UpdateAccountDto(
    string Name
);

public record AccountSearchDto(
    int PageNumber = 1,
    int PageSize = 10,
    string? SearchTerm = null,
    Guid? UserId = null
);
