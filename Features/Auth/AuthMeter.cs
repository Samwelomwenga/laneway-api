using System.Diagnostics.Metrics;

namespace Laneway.Api;

public sealed class AuthMeter(IMeterFactory factory)
{
    public const string Name = "Laneway.Auth";

    public Meter Meter { get; } = factory.Create(Name);
}
