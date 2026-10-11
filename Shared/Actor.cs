namespace Laneway.Api;

public sealed class Actor
{
    private Guid? _id;

    public Guid Id => _id ?? throw new InvalidOperationException(
        "This request has no actor. A bearer token resolves one, and so does X-User-Id on a POST, PUT, or DELETE.");

    public bool IsResolved => _id is not null;

    public void Resolve(Guid id) => _id = id;
}
