namespace Laneway.Api;

public enum CopyRun
{
    Done,
    Refused,
    Lost
}

public sealed record CopyRunResult(CopyRun Outcome, List<ApiError>? Errors)
{
    public static CopyRunResult Done { get; } = new(CopyRun.Done, null);

    public static CopyRunResult Lost { get; } = new(CopyRun.Lost, null);

    public static CopyRunResult Refused(List<ApiError> errors) => new(CopyRun.Refused, errors);
}

public interface ICopyJobRunner
{
    CopyJobKind Kind { get; }

    Task<CopyRunResult> RunAsync(CopyJob job, int attempt, CancellationToken token);
}
