namespace Laneway.Api;

public record AccountDto(
    Guid Id,
    string Name,
    Guid UserId,
    Guid CreatedBy,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid? UpdatedBy
);

public record CreateAccountDto(
    string Name
);

public record UpdateAccountDto(
    string Name
);

public record AccountSearchDto(
    int PageNumber = 1,
    int PageSize = QueryReader.DefaultPageSize,
    string? SearchTerm = null
);
