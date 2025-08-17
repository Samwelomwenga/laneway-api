namespace DefaultNamespace;

public record BoardDto
(
    Guid Id,
    string Name,
    string Description,
    string? Title,
    Guid WorkspaceId,
    Guid OwnerId,
    string Visibility,
    bool IsArchived,
    List<Guid> ListIds,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy
);

public record CreateBoardDto
(
    string Name,
    string Description,
    string? Title,
    Guid WorkspaceId,
    Guid OwnerId,
    string Visibility,
    bool IsArchived
);

public record UpdateBoardDto
(
    string Name,
    string Description,
    string? Title,
    Guid WorkspaceId,
    Guid OwnerId,
    string Visibility,
    bool IsArchived
);

public record BoardSearchDto
(
    int PageNumber = 1,
    int PageSize = 10,
    string? SearchTerm = null,
    Guid? WorkspaceId = null,
    bool? IsArchived = null,
    string? Visibility = null
);
