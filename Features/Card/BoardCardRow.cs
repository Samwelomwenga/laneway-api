namespace Laneway.Api;

public sealed record BoardCardRow
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
    Guid? CoverAttachmentId,
    Color? CoverColor,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy
);
