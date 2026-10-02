using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

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
    BoardVisibility? Visibility
)
{
    public string? Name { get; init; } = Name?.Trim();
}

public record UpdateBoardDto
(
    string? Name,
    string? Description,
    BoardVisibility? Visibility
)
{
    public string? Name { get; init; } = Name?.Trim();
}

public record MoveBoardDto
(
    Guid? WorkspaceId
);

public record CopyBoardDto
(
    Guid? WorkspaceId,
    string? Name,
    List<BoardCopyPart>? Keep
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
    ArchiveFilter Archived,
    BoardVisibility? Visibility
) : ISearchQuery<BoardSearchDto>
{
    public static BoardSearchDto Read(QueryReader query) => new(
        query.PageNumber(),
        query.PageSize(),
        query.Text("searchTerm"),
        query.Id("workspaceId"),
        query.EnumName<ArchiveFilter>("archived") ?? ArchiveFilter.Exclude,
        query.EnumName<BoardVisibility>("visibility"));
}
