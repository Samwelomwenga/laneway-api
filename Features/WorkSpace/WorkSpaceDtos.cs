using Microsoft.AspNetCore.Mvc;

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
[ModelBinder(typeof(SearchQueryBinder<WorkSpaceSearchDto>))]
public record WorkSpaceSearchDto
(
    int PageNumber,
    int PageSize,
    string? SearchTerm,
    bool? IsArchived,
    WorkspaceVisibility? Visibility
) : ISearchQuery<WorkSpaceSearchDto>
{
    public static WorkSpaceSearchDto Read(QueryReader query) => new(
        query.PageNumber(),
        query.PageSize(),
        query.Text("searchTerm"),
        query.Flag("isArchived"),
        query.EnumName<WorkspaceVisibility>("visibility"));
}
