namespace Laneway.Api;

public sealed class Actor
{
    private Guid? _id;

    public Guid Id => _id ?? throw new InvalidOperationException(
        "This request has no actor. Only POST, PUT, and DELETE resolve one.");

    public void Resolve(Guid id) => _id = id;
}
