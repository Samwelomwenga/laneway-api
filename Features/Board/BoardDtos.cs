using System.Text.Json.Serialization;
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
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy
);

public record BoardViewDto : BoardDto
{
    public BoardViewDto(BoardDto board, List<LabelDto> labels, List<BoardListDto> lists) : base(board)
    {
        Labels = labels;
        Lists = lists;
    }

    [JsonPropertyOrder(1)]
    public List<LabelDto> Labels { get; init; }

    [JsonPropertyOrder(1)]
    public List<BoardListDto> Lists { get; init; }
}

public record CreateBoardDto
(
    string? Name,
    string? Description,
    BoardVisibility? Visibility,
    BoardCommenting? Commenting,
    BoardAddingMembers? AddingMembers,
    bool? SelfJoin
)
{
    public string? Name { get; init; } = Name?.Trim();
}

public record UpdateBoardDto
(
    string? Name,
    string? Description
)
{
    public string? Name { get; init; } = Name?.Trim();
}

public record BoardSettingsDto
(
    BoardCommenting Commenting,
    BoardAddingMembers AddingMembers,
    bool SelfJoin,
    BoardVisibility Visibility
);

public record UpdateBoardSettingsDto
(
    BoardCommenting? Commenting,
    BoardAddingMembers? AddingMembers,
    bool? SelfJoin,
    BoardVisibility? Visibility
);

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
    ArchiveFilter Archived,
    BoardVisibility? Visibility
) : ISearchQuery<BoardSearchDto>
{
    public static BoardSearchDto Read(QueryReader query) => new(
        query.PageNumber(),
        query.PageSize(),
        query.Text("searchTerm"),
        query.EnumName<ArchiveFilter>("archived") ?? ArchiveFilter.Exclude,
        query.EnumName<BoardVisibility>("visibility"));
}
