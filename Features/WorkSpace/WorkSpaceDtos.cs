namespace DefaultNamespace;

public record WorkSpaceDto
(
    Guid Id,
    string Name,
    string Description,
    WorkspaceVisibility Visibility,
    bool IsArchived,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy,
    List<Guid> BoardIds
);
public record CreateWorkSpaceDto
(
    string? Name,
    string? Description,
    WorkspaceVisibility? Visibility,
    bool? IsArchived
)
{
    public string? Name { get; init; } = Name?.Trim();
}
public record UpdateWorkSpaceDto
(
    string? Name,
    string? Description,
    WorkspaceVisibility? Visibility,
    bool? IsArchived
)
{
    public string? Name { get; init; } = Name?.Trim();
}
public record WorkSpaceSearchDto
(
    int PageNumber = 1,
    int PageSize = 10,
    string? SearchTerm = null,
    bool? IsArchived = null,
    WorkspaceVisibility? Visibility = null
);
