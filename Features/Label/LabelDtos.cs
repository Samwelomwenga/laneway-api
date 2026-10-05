using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

public record LabelDto
(
    Guid Id,
    string Name,
    Guid BoardId,
    Color? Color,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy
);

public record CreateLabelDto
(
    string? Name,
    Guid? BoardId,
    Color? Color
)
{
    public string? Name { get; init; } = Name?.Trim();
}

public record UpdateLabelDto
(
    string? Name,
    Color? Color
)
{
    public string? Name { get; init; } = Name?.Trim();
}

[ModelBinder(typeof(SearchQueryBinder<LabelSearchDto>))]
public record LabelSearchDto
(
    int PageNumber,
    int PageSize,
    string? SearchTerm,
    Guid? BoardId
) : ISearchQuery<LabelSearchDto>
{
    public static LabelSearchDto Read(QueryReader query) => new(
        query.PageNumber(),
        query.PageSize(),
        query.Text("searchTerm"),
        query.Id("boardId"));
}
