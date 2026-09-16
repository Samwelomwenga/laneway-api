using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

public record ListDto
(
    Guid Id,
    string Name,
    double Position,
    Guid BoardId,
    Color? Color,
    bool IsArchived,
    List<Guid> CardIds,
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
    Guid? BoardId,
    Color? Color,
    bool? IsArchived
)
{
    public string? Name { get; init; } = Name?.Trim();
}

public record UpdateListDto
(
    string? Name,
    double? Position,
    Guid? BoardId,
    Color? Color,
    bool? IsArchived
)
{
    public string? Name { get; init; } = Name?.Trim();
}
[ModelBinder(typeof(SearchQueryBinder<ListSearchDto>))]
public record ListSearchDto
(
    int PageNumber,
    int PageSize,
    string? SearchTerm,
    Guid? BoardId,
    bool? IsArchived
) : ISearchQuery<ListSearchDto>
{
    public static ListSearchDto Read(QueryReader query) => new(
        query.PageNumber(),
        query.PageSize(),
        query.Text("searchTerm"),
        query.Id("boardId"),
        query.Flag("isArchived"));
}
