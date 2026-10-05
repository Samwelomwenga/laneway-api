namespace Laneway.Api;

public sealed class CopyJobSignal
{
    private readonly SemaphoreSlim _waiting = new(0, 1);

    public void Poke()
    {
        try
        {
            _waiting.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken token) => _waiting.WaitAsync(timeout, token);
}
