using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

public record ListDto
(
    Guid Id,
    string Name,
    double Position,
    Guid BoardId,
    Color? Color,
    bool IsArchived,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy
);

public record CreateListDto
(
    string? Name,
    PositionValue? Position,
    Guid? Before,
    Guid? After,
    Color? Color
) : IPlacing
{
    public string? Name { get; init; } = Name?.Trim();
}

public record UpdateListDto
(
    string? Name,
    Color? Color
)
{
    public string? Name { get; init; } = Name?.Trim();
}

public record MoveListDto
(
    Guid? BoardId,
    PositionValue? Position,
    Guid? Before,
    Guid? After
) : IPlacing;
public record CopyListDto
(
    Guid? BoardId,
    string? Name,
    List<CopyPart>? Keep,
    PositionValue? Position,
    Guid? Before,
    Guid? After
) : IPlacing
{
    public string? Name { get; init; } = Name?.Trim();
}

[ModelBinder(typeof(SearchQueryBinder<ListSearchDto>))]
public record ListSearchDto
(
    int PageNumber,
    int PageSize,
    string? SearchTerm,
    ArchiveFilter Archived
) : ISearchQuery<ListSearchDto>
{
    public static ListSearchDto Read(QueryReader query) => new(
        query.PageNumber(),
        query.PageSize(),
        query.Text("searchTerm"),
        query.EnumName<ArchiveFilter>("archived") ?? ArchiveFilter.Exclude);
}
