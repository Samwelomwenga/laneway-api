namespace DefaultNamespace;

public record CardDto
(
     Guid Id,
     string Title,
     string Description ,
     DateTime DueDate ,
     int Position ,
     Guid ListId ,
     bool Status ,
     string? Cover,
     DateTime? StartDate,
     DateTime? EndDate,
     DateTime? ReminderDate ,
     bool IsArchived ,
     List<Guid> LabelIds,
      DateTime CreatedAt ,
     DateTime? UpdatedAt ,
     Guid CreatedBy ,
     Guid? UpdatedBy
);

public record CreateCardDto
(
    string Title,
    string Description,
    DateTime DueDate,
    int Position,
    Guid ListId,
    bool Status,
    string? Cover,
    DateTime? StartDate,
    DateTime? EndDate,
    DateTime? ReminderDate,
    bool IsArchived,
    List<Guid>? LabelIds
);

public record UpdateCardDto
(
    string Title,
    string Description,
    DateTime DueDate,
    int Position,
    Guid ListId,
    bool Status,
    string? Cover,
    DateTime? StartDate,
    DateTime? EndDate,
    DateTime? ReminderDate,
    bool IsArchived,
    List<Guid>? LabelIds
);
public record CardSearchDto
(
    int PageNumber = 1,
    int PageSize = 10,
    string? searchTerm = null,
    DateTime? DueDate = null,
    int? Position = null,
    Guid? ListId = null,
    bool? Status = null,
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    DateTime? ReminderDate = null,
    bool? IsArchived = null
);
