namespace DefaultNamespace;

public record LabelDto
(
    Guid Id,
    string Name,
    string Color,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy
);

public record CreateLabelDto
(
    string Name,
    string Color
);

public record UpdateLabelDto
(
    string Name,
    string Color
);

public record LabelSearchDto
(
    int PageNumber = 1,
    int PageSize = 10,
    string? SearchTerm = null,
);
