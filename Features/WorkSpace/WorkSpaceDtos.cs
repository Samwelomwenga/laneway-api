namespace DefaultNamespace;

public record WorkSpaceDto
(
    Guid Id,
    string Name,
    string? Description,
    string Visibility,
    bool IsArchived,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid CreatedBy,
    Guid? UpdatedBy,
    List<Guid> BoardIds
);
public record CreateWorkSpaceDto
(
    string Name,
    string? Description,
    string Visibility,
    bool IsArchived
);
public record UpdateWorkSpaceDto
(
    string Name,
    string? Description,
    string Visibility,
    bool IsArchived
);
public record WorkSpaceSearchDto
(
    int PageNumber = 1,
    int PageSize = 10,
    string? SearchTerm = null,
    bool? IsArchived = null,
    string? Visibility = null
);
