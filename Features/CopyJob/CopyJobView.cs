using System.Text.Json;

namespace DefaultNamespace;

public static class CopyJobView
{
    private static readonly JsonSerializerOptions ErrorOptions = new(JsonSerializerDefaults.Web);

    public static CopyJobDto Of(CopyJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return new CopyJobDto(
            job.Id,
            job.Kind,
            job.SourceId,
            job.Status,
            job.ResultId,
            ErrorsOf(job),
            job.CreatedAt,
            job.CreatedBy,
            job.StartedAt,
            job.FinishedAt);
    }

    public static JsonDocument Stored(List<ApiError> errors) =>
        JsonSerializer.SerializeToDocument(errors, ErrorOptions);

    private static List<ApiError>? ErrorsOf(CopyJob job) =>
        job.Errors is null ? null : job.Errors.Deserialize<List<ApiError>>(ErrorOptions);
}
