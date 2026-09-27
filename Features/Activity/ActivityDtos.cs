using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

public record ActivityEntryDto
(
    Guid Id,
    ActivityType Type,
    DateTime CreatedAt,
    Guid CreatedBy,
    Guid? WorkspaceId,
    Guid? BoardId,
    Guid? ListId,
    Guid? CardId,
    Guid? FromWorkspaceId,
    Guid? FromBoardId,
    Guid? FromListId,
    JsonElement Data,
    string? Text,
    DateTime? UpdatedAt
);

[ModelBinder(typeof(SearchQueryBinder<ActivitySearchDto>))]
public record ActivitySearchDto
(
    int PageNumber,
    int PageSize,
    Guid? WorkspaceId,
    Guid? BoardId,
    Guid? ListId,
    Guid? CardId,
    List<ActivityType> Types,
    DateTimeOffset? Before,
    DateTimeOffset? Since
) : ISearchQuery<ActivitySearchDto>
{
    public static ActivitySearchDto Read(QueryReader query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new ActivitySearchDto(
            query.PageNumber(),
            query.PageSize(),
            query.Id("workspaceId"),
            query.Id("boardId"),
            query.Id("listId"),
            query.Id("cardId"),
            query.EnumNames<ActivityType>("type"),
            query.Timestamp("before"),
            query.Timestamp("since"));
    }
}
