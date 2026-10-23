using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Laneway.Api;

public static class RateLimits
{
    public const string SignIn = "sign-in";

    public const string SignUp = "sign-up";

    public const string Refresh = "refresh";

    public const string PasswordCheck = "password-check";

    public const string AddMember = "add-member";

    public const string Webhook = "webhook";

    public const string SignedIn = "signed-in";

    public const string Anonymous = "anonymous";

    private const string TryLater = "Too many requests. Try again after the Retry-After delay.";

    private const string NoIp = "none";

    private static readonly RateLimit SignedInLimit = new(SignedIn, 300, TimeSpan.FromMinutes(1), CountsBy.Sub);

    private static readonly RateLimit AnonymousLimit = new(Anonymous, 60, TimeSpan.FromMinutes(1), CountsBy.Ip);

    private static readonly Dictionary<string, RateLimit> RouteLimits = new RateLimit[]
    {
        new(SignIn, 10, TimeSpan.FromMinutes(1), CountsBy.Ip),
        new(SignUp, 10, TimeSpan.FromHours(1), CountsBy.Ip),
        new(Refresh, 60, TimeSpan.FromMinutes(1), CountsBy.Ip),
        new(PasswordCheck, 5, TimeSpan.FromMinutes(1), CountsBy.Sub),
        new(AddMember, 20, TimeSpan.FromMinutes(1), CountsBy.Sub),
        new(Webhook, 600, TimeSpan.FromMinutes(1), CountsBy.Ip)
    }.ToDictionary(limit => limit.Policy, StringComparer.Ordinal);

    public static IServiceCollection AddRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = new StackedRateLimiter(Buckets);
            options.OnRejected = RejectAsync;
        });

    private static IEnumerable<Bucket> Buckets(HttpContext context)
    {
        var userId = ActorOf(context);
        var route = context.GetEndpoint()?.Metadata.GetMetadata<RateLimitedAttribute>() is { } attribute
            ? RouteLimits[attribute.Policy]
            : null;

        var routeKey = route?.CountsBy == CountsBy.Sub ? userId?.ToString() : IpKey(context);
        if (route is not null && routeKey is not null)
        {
            yield return new Bucket(route, routeKey);
        }

        if (userId is { } id)
        {
            yield return new Bucket(SignedInLimit, id.ToString());
        }
        else if (route is not { CountsBy: CountsBy.Ip })
        {
            yield return new Bucket(AnonymousLimit, IpKey(context));
        }
    }

    private static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        if (!context.Lease.TryGetMetadata(RefusedLease.RefusedBy, out var limit) || limit is null)
        {
            throw new InvalidOperationException("A refused lease didn't name its rate limit.");
        }

        var http = context.HttpContext;
        var services = http.RequestServices;
        services.GetRequiredService<ILoggerFactory>().CreateLogger(AuthLog.Category)
            .RateLimited(limit.Policy, limit.CountsBy == CountsBy.Sub ? ActorOf(http) : null);
        services.GetRequiredService<AuthMeter>().RateLimited(limit.Policy);

        http.Response.Headers.RetryAfter = Math.Ceiling(limit.Segment.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        await http.Response.WriteAsJsonAsync(
            ApiResponse<object>.ErrorResponse(
                TryLater,
                StatusCodes.Status429TooManyRequests,
                [new ApiError(null, ErrorCodes.TooManyRequests, TryLater)]),
            cancellationToken);
    }

    private static Guid? ActorOf(HttpContext context)
    {
        var actor = context.RequestServices.GetRequiredService<Actor>();
        return actor.IsResolved ? actor.Id : null;
    }

    // One line usually gets a whole IPv6 /64, so counting each address would hand a client billions of buckets.
    private static string IpKey(HttpContext context) => context.Connection.RemoteIpAddress switch
    {
        null => NoIp,
        { IsIPv4MappedToIPv6: true } address => address.MapToIPv4().ToString(),
        { AddressFamily: AddressFamily.InterNetworkV6 } address => Prefix64(address),
        var address => address.ToString()
    };

    private static string Prefix64(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class RateLimitedAttribute(string policy) : Attribute
{
    public string Policy { get; } = policy;
}

internal enum CountsBy
{
    Ip,
    Sub
}

internal sealed record RateLimit(string Policy, int PermitLimit, TimeSpan Window, CountsBy CountsBy)
{
    private const int Segments = 6;

    public TimeSpan Segment => Window / Segments;

    public RateLimitPartition<string> Partition(string key) =>
        RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = PermitLimit,
            Window = Window,
            SegmentsPerWindow = Segments,
            QueueLimit = 0
        });
}

internal sealed record Bucket(RateLimit Limit, string Key);

// A window limiter keeps a permit after its lease is disposed, so taking one bucket's permit and then
// failing on the next would spend it for nothing.
internal sealed class StackedRateLimiter(Func<HttpContext, IEnumerable<Bucket>> buckets)
    : PartitionedRateLimiter<HttpContext>
{
    private readonly Lock _gate = new();

    private readonly Dictionary<RateLimit, PartitionedRateLimiter<string>> _limiters = [];

    public override RateLimiterStatistics? GetStatistics(HttpContext resource) => null;

    protected override RateLimitLease AttemptAcquireCore(HttpContext resource, int permitCount)
    {
        var wanted = buckets(resource).ToList();
        lock (_gate)
        {
            if (wanted.Find(bucket => !Has(bucket, permitCount)) is { } full)
            {
                return new RefusedLease(full.Limit);
            }

            foreach (var bucket in wanted)
            {
                using var lease = LimiterFor(bucket.Limit).AttemptAcquire(bucket.Key, permitCount);
                if (!lease.IsAcquired)
                {
                    throw new InvalidOperationException($"The {bucket.Limit.Policy} limit had a permit and then refused it.");
                }
            }
        }

        return GrantedLease.Instance;
    }

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(
        HttpContext resource, int permitCount, CancellationToken cancellationToken) =>
        ValueTask.FromResult(AttemptAcquireCore(resource, permitCount));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                foreach (var limiter in _limiters.Values)
                {
                    limiter.Dispose();
                }
            }
        }

        base.Dispose(disposing);
    }

    private bool Has(Bucket bucket, int permitCount) =>
        LimiterFor(bucket.Limit).GetStatistics(bucket.Key)?.CurrentAvailablePermits >= permitCount;

    private PartitionedRateLimiter<string> LimiterFor(RateLimit limit)
    {
        if (!_limiters.TryGetValue(limit, out var limiter))
        {
            limiter = PartitionedRateLimiter.Create<string, string>(limit.Partition);
            _limiters[limit] = limiter;
        }

        return limiter;
    }
}

internal sealed class GrantedLease : RateLimitLease
{
    public static readonly GrantedLease Instance = new();

    public override bool IsAcquired => true;

    public override IEnumerable<string> MetadataNames => [];

    public override bool TryGetMetadata(string metadataName, out object? metadata)
    {
        metadata = null;
        return false;
    }
}

internal sealed class RefusedLease(RateLimit limit) : RateLimitLease
{
    public static readonly MetadataName<RateLimit> RefusedBy = new("laneway.refused-by");

    public override bool IsAcquired => false;

    public override IEnumerable<string> MetadataNames => [RefusedBy.Name];

    public override bool TryGetMetadata(string metadataName, out object? metadata)
    {
        metadata = metadataName == RefusedBy.Name ? limit : null;
        return metadata is not null;
    }
}
