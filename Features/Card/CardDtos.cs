namespace DefaultNamespace;

public record CardDto
(
     Guid Id,
     string Title,
     string Description ,
     DateTime? DueDate ,
     int Position ,
     Guid ListId ,
     bool IsDueComplete ,
     string? Cover,
     DateTime? StartDate,
     int? DueReminderMinutes ,
     bool IsArchived ,
     List<Guid> LabelIds,
      DateTime CreatedAt ,
     DateTime? UpdatedAt ,
     Guid CreatedBy ,
     Guid? UpdatedBy
);

public record CreateCardDto
(
    string? Title,
    string? Description,
    DateTime? DueDate,
    int? Position,
    Guid? ListId,
    bool? IsDueComplete,
    string? Cover,
    DateTime? StartDate,
    int? DueReminderMinutes,
    bool? IsArchived,
    List<Guid?>? LabelIds
)
{
    public string? Title { get; init; } = Title?.Trim();
}

public record UpdateCardDto
(
    string? Title,
    string? Description,
    DateTime? DueDate,
    int? Position,
    Guid? ListId,
    bool? IsDueComplete,
    string? Cover,
    DateTime? StartDate,
    int? DueReminderMinutes,
    bool? IsArchived,
    List<Guid?>? LabelIds
)
{
    public string? Title { get; init; } = Title?.Trim();
}
public record CardSearchDto
(
    int PageNumber = 1,
    int PageSize = 10,
    string? searchTerm = null,
    DateTime? DueDate = null,
    int? Position = null,
    Guid? ListId = null,
    bool? IsDueComplete = null,
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    bool? IsArchived = null
);
