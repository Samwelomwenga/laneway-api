namespace Laneway.Api;

public sealed class Actor
{
    private Guid? _id;
    private Guid? _sessionId;

    public Guid Id => _id ?? throw new InvalidOperationException(
        "This request has no actor because it isn't authenticated. Only a bearer token resolves one.");

    public Guid SessionId => _sessionId ?? throw new InvalidOperationException(
        "This request has no session. Only a bearer token carries one.");

    public bool IsResolved => _id is not null;

    public bool HasSession => _sessionId is not null;

    public void Resolve(Guid id, Guid? sessionId = null)
    {
        _id = id;
        _sessionId = sessionId;
    }
}
