namespace DefaultNamespace;

public record CopyJobDto
(
    Guid Id,
    CopyJobKind Kind,
    Guid SourceId,
    CopyJobStatus Status,
    Guid? ResultId,
    List<ApiError>? Errors,
    DateTime CreatedAt,
    Guid CreatedBy,
    DateTime? StartedAt,
    DateTime? FinishedAt
);
