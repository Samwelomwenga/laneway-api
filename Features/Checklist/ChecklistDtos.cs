using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

public record ChecklistDto
(
    Guid Id,
    Guid CardId,
    string Name,
    double Position,
    bool IsArchived,
    List<CheckItemDto> CheckItems,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy
);

public record CreateChecklistDto
(
    string? Name,
    PositionValue? Position,
    Guid? Before,
    Guid? After,
    Guid? CardId
) : IPlacing
{
    public string? Name { get; init; } = Name?.Trim();
}

public record UpdateChecklistDto
(
    string? Name
)
{
    public string? Name { get; init; } = Name?.Trim();
}

public record ReorderChecklistDto
(
    PositionValue? Position,
    Guid? Before,
    Guid? After
) : IPlacing;
[ModelBinder(typeof(SearchQueryBinder<ChecklistSearchDto>))]
public record ChecklistSearchDto
(
    int PageNumber,
    int PageSize,
    Guid? CardId,
    ArchiveFilter Archived
) : ISearchQuery<ChecklistSearchDto>
{
    public static ChecklistSearchDto Read(QueryReader query) => new(
        query.PageNumber(),
        query.PageSize(),
        query.Id("cardId"),
        query.EnumName<ArchiveFilter>("archived") ?? ArchiveFilter.Exclude);
}
