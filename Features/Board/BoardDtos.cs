using Microsoft.AspNetCore.Mvc;

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

[ModelBinder(typeof(SearchQueryBinder<BoardSearchDto>))]
public record BoardSearchDto
(
    int PageNumber,
    int PageSize,
    string? SearchTerm,
    Guid? WorkspaceId,
    bool? IsArchived,
    BoardVisibility? Visibility
) : ISearchQuery<BoardSearchDto>
{
    public static BoardSearchDto Read(QueryReader query) => new(
        query.PageNumber(),
        query.PageSize(),
        query.Text("searchTerm"),
        query.Id("workspaceId"),
        query.Flag("isArchived"),
        query.EnumName<BoardVisibility>("visibility"));
}
