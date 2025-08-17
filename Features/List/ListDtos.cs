namespace DefaultNamespace;

public record ListDto
(
    Guid Id,
    string Name,
    int Position,
    Guid BoardId,
    string Color,
    bool IsArchived,
    List<Guid> CardIds,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy
);

public record CreateListDto
(
    string Name,
    int Position,
    Guid BoardId,
    string Color,
    bool IsArchived
);

public record UpdateListDto
(
    string Name,
    int Position,
    Guid BoardId,
    string Color,
    bool IsArchived
);
public record ListSearchDto
(
    int PageNumber = 1,
    int PageSize = 10,
    string? SearchTerm = null,
    Guid? BoardId = null,
    bool? IsArchived = null
);
