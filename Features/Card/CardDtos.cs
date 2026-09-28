using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

public record CardDto
(
     Guid Id,
     string Title,
     string Description ,
     DateTime? DueDate ,
     double Position ,
     Guid ListId ,
     bool IsDueComplete ,
     DateTime? StartDate,
     int? DueReminderMinutes ,
     bool IsArchived ,
     List<Guid> LabelIds,
     int CheckItemCount,
     int CheckedItemCount,
     int AttachmentCount,
     int CommentCount,
     CardCoverDto? Cover,
      DateTime CreatedAt ,
     DateTime? UpdatedAt ,
     Guid CreatedBy ,
     Guid? UpdatedBy
);

public record CardCoverDto
(
    Guid? AttachmentId,
    Color? Color,
    string? Url
);

public record CoverDto
(
    Guid? AttachmentId,
    Color? Color
);

public record CreateCardDto
(
    string? Title,
    string? Description,
    DateTime? DueDate,
    PositionValue? Position,
    Guid? Before,
    Guid? After,
    Guid? ListId,
    bool? IsDueComplete,
    DateTime? StartDate,
    int? DueReminderMinutes,
    List<Guid?>? LabelIds
) : IPlacing
{
    public string? Title { get; init; } = Title?.Trim();
}

public record UpdateCardDto
(
    string? Title,
    string? Description,
    DateTime? DueDate,
    bool? IsDueComplete,
    DateTime? StartDate,
    int? DueReminderMinutes,
    List<Guid?>? LabelIds
)
{
    public string? Title { get; init; } = Title?.Trim();
}

public record MoveCardDto
(
    Guid? ListId,
    PositionValue? Position,
    Guid? Before,
    Guid? After
) : IPlacing;
public record CopyCardDto
(
    Guid? ListId,
    string? Title,
    List<CopyPart>? Keep,
    PositionValue? Position,
    Guid? Before,
    Guid? After
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
    Guid? ListId,
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
        query.Id("listId"),
        query.EnumName<ArchiveFilter>("archived") ?? ArchiveFilter.Exclude,
        query.Day("dueDate"),
        query.Timestamp("dueBefore"),
        query.Timestamp("startFrom"),
        query.Flag("isDueComplete"));
}
