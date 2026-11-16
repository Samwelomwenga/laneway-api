using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

public record CardDto
(
     Guid Id,
     string Title,
     string Description,
     DateTime? DueDate,
     double Position,
     Guid ListId,
     bool IsDueComplete,
     DateTime? StartDate,
     int? DueReminderMinutes,
     bool IsArchived,
     List<Guid> LabelIds,
     int CheckItemCount,
     int CheckedItemCount,
     int AttachmentCount,
     int CommentCount,
     CardCoverDto? Cover,
      DateTime CreatedAt,
     DateTime? UpdatedAt,
     Guid CreatedBy,
     Guid? UpdatedBy
);

public record BoardCardDto
(
    Guid Id,
    string Title,
    bool HasDescription,
    DateTime? DueDate,
    double Position,
    Guid ListId,
    bool IsDueComplete,
    DateTime? StartDate,
    int? DueReminderMinutes,
    bool IsArchived,
    List<Guid> LabelIds,
    int CheckItemCount,
    int CheckedItemCount,
    int AttachmentCount,
    int CommentCount,
    CardCoverDto? Cover,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy
);

public record CardViewDto : CardDto
{
    public CardViewDto(CardDto card, List<ChecklistDto> checklists, List<AttachmentDto> attachments) : base(card)
    {
        Checklists = checklists;
        Attachments = attachments;
    }

    [JsonPropertyOrder(1)]
    public List<ChecklistDto> Checklists { get; init; }

    [JsonPropertyOrder(1)]
    public List<AttachmentDto> Attachments { get; init; }
}

public record CardCoverDto
(
    Guid? AttachmentId,
    Color? Color,
    string? Url
);

public record CoverDto
(
    Guid? AttachmentId = null,
    Color? Color = null
);

public record CreateCardDto
(
    string? Title,
    bool? IsDueComplete,
    string? Description = null,
    DateTime? DueDate = null,
    PositionValue? Position = null,
    Guid? Before = null,
    Guid? After = null,
    DateTime? StartDate = null,
    int? DueReminderMinutes = null,
    List<Guid?>? LabelIds = null
) : IPlacing
{
    public string? Title { get; init; } = Title?.Trim();
}

public record UpdateCardDto
(
    string? Title,
    bool? IsDueComplete,
    string? Description = null,
    DateTime? DueDate = null,
    DateTime? StartDate = null,
    int? DueReminderMinutes = null,
    List<Guid?>? LabelIds = null
)
{
    public string? Title { get; init; } = Title?.Trim();
}

public record MoveCardDto
(
    Guid? ListId,
    PositionValue? Position = null,
    Guid? Before = null,
    Guid? After = null
) : IPlacing;
public record CopyCardDto
(
    Guid? ListId,
    string? Title = null,
    List<CopyPart>? Keep = null,
    PositionValue? Position = null,
    Guid? Before = null,
    Guid? After = null
) : IPlacing
{
    public string? Title { get; init; } = Title?.Trim();
}

[ModelBinder(typeof(SearchQueryBinder<CardSearchDto>))]
public record CardSearchDto
(
    int PageNumber,
    int PageSize,
    string? SearchTerm,
    ArchiveFilter Archived,
    DateOnly? DueDate,
    DateTimeOffset? DueBefore,
    DateTimeOffset? StartFrom,
    bool? IsDueComplete
) : ISearchQuery<CardSearchDto>
{
    public static CardSearchDto Read(QueryReader query) => new(
        query.PageNumber(),
        query.PageSize(),
        query.Text("searchTerm"),
        query.EnumName<ArchiveFilter>("archived") ?? ArchiveFilter.Exclude,
        query.Day("dueDate"),
        query.Timestamp("dueBefore"),
        query.Timestamp("startFrom"),
        query.Flag("isDueComplete"));
}
