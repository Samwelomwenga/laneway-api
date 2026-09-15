namespace DefaultNamespace;

public record LabelDto
(
    Guid Id,
    string Name,
    Color? Color,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy
);

public record CreateLabelDto
(
    string? Name,
    Color? Color
)
{
    public string? Name { get; init; } = Name?.Trim();
}

public record UpdateLabelDto
(
    string? Name,
    Color? Color
)
{
    public string? Name { get; init; } = Name?.Trim();
}

public record LabelSearchDto
(
    int PageNumber = 1,
    int PageSize = 10,
    string? SearchTerm = null
);
