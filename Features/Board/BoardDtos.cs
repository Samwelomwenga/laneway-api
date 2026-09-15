namespace DefaultNamespace;

public record BoardDto
(
    Guid Id,
    string Name,
    string Description,
    Guid WorkspaceId,
    BoardVisibility Visibility,
    bool IsArchived,
    List<Guid> ListIds,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy
);

public record CreateBoardDto
(
    string? Name,
    string? Description,
    Guid? WorkspaceId,
    BoardVisibility? Visibility,
    bool? IsArchived
)
{
    public string? Name { get; init; } = Name?.Trim();
}

public record UpdateBoardDto
(
    string? Name,
    string? Description,
    Guid? WorkspaceId,
    BoardVisibility? Visibility,
    bool? IsArchived
)
{
    public string? Name { get; init; } = Name?.Trim();
}

public record BoardSearchDto
(
    int PageNumber = 1,
    int PageSize = 10,
    string? SearchTerm = null,
    Guid? WorkspaceId = null,
    bool? IsArchived = null,
    BoardVisibility? Visibility = null
);
