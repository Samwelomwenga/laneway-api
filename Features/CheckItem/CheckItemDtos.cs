using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

public record CheckItemDto
(
    Guid Id,
    Guid ChecklistId,
    string Name,
    double Position,
    bool IsChecked,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy
);

public record CreateCheckItemDto
(
    string? Name,
    bool? IsChecked,
    PositionValue? Position,
    Guid? Before,
    Guid? After,
    Guid? ChecklistId
) : IPlacing
{
    public string? Name { get; init; } = Name?.Trim();
}

public record UpdateCheckItemDto
(
    string? Name
)
{
    public string? Name { get; init; } = Name?.Trim();
}

public record MoveCheckItemDto
(
    Guid? ChecklistId,
    PositionValue? Position,
    Guid? Before,
    Guid? After
) : IPlacing;

public record CheckedDto(bool? Value);

[ModelBinder(typeof(SearchQueryBinder<CheckItemSearchDto>))]
public record CheckItemSearchDto
(
    int PageNumber,
    int PageSize,
    Guid? ChecklistId,
    CheckItemArchiveFilter Archived
) : ISearchQuery<CheckItemSearchDto>
{
    public static CheckItemSearchDto Read(QueryReader query) => new(
        query.PageNumber(),
        query.PageSize(),
        query.Id("checklistId"),
        query.EnumName<CheckItemArchiveFilter>("archived") ?? CheckItemArchiveFilter.Exclude);
}

public static class CheckItemView
{
    public static CheckItemDto Of(CheckItem checkItem)
    {
        ArgumentNullException.ThrowIfNull(checkItem);

        return new CheckItemDto(
            checkItem.Id,
            checkItem.ChecklistId,
            checkItem.Name,
            checkItem.Position,
            checkItem.IsChecked,
            checkItem.CreatedAt,
            checkItem.UpdatedAt,
            checkItem.CreatedBy,
            checkItem.UpdatedBy
        );
    }

    public static List<CheckItemDto> Sorted(IEnumerable<CheckItem> checkItems)
    {
        ArgumentNullException.ThrowIfNull(checkItems);

        return checkItems.InSortOrder().Select(Of).ToList();
    }
}
