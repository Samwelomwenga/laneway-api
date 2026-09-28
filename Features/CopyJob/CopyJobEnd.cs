namespace DefaultNamespace;

public static class CopyJobEnd
{
    public static ApiError InternalError { get; } =
        new(null, ErrorCodes.InternalError, "The copy could not finish, and nothing was created.");

    public static void Fail(CopyJob job, List<ApiError> errors)
    {
        ArgumentNullException.ThrowIfNull(job);

        job.Status = CopyJobStatus.Failed;
        job.Errors = CopyJobView.Stored(errors);
        job.FinishedAt = DateTime.UtcNow;
    }
}
