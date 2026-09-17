namespace DefaultNamespace;

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
