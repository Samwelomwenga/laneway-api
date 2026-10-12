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

    public const string SignedIn = "signed-in";

    public const string Anonymous = "anonymous";

    private const string TryLater = "Too many requests. Try again after the Retry-After delay.";

    private const string NoIp = "none";

    private static readonly RateLimit SignInLimit = new(SignIn, 10, TimeSpan.FromMinutes(1), CountsBy.Ip);

    private static readonly RateLimit SignUpLimit = new(SignUp, 10, TimeSpan.FromHours(1), CountsBy.Ip);

    private static readonly RateLimit RefreshLimit = new(Refresh, 60, TimeSpan.FromMinutes(1), CountsBy.Ip);

    private static readonly RateLimit PasswordCheckLimit = new(PasswordCheck, 5, TimeSpan.FromMinutes(1), CountsBy.Sub);

    private static readonly RateLimit SignedInLimit = new(SignedIn, 300, TimeSpan.FromMinutes(1), CountsBy.Sub);

    private static readonly RateLimit AnonymousLimit = new(Anonymous, 60, TimeSpan.FromMinutes(1), CountsBy.Ip);

    private static readonly HashSet<string> AnonymousAuthPolicies = [SignIn, SignUp, Refresh];

    public static IServiceCollection AddRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(GlobalPartition);
            options.OnRejected = (context, cancellationToken) => RejectAsync(
                context, ActorOf(context.HttpContext) is null ? AnonymousLimit : SignedInLimit, cancellationToken);
            options.AddPolicy(SignIn, new RateLimitPolicy(SignInLimit));
            options.AddPolicy(SignUp, new RateLimitPolicy(SignUpLimit));
            options.AddPolicy(Refresh, new RateLimitPolicy(RefreshLimit));
            options.AddPolicy(PasswordCheck, new RateLimitPolicy(PasswordCheckLimit));
        });

    private static RateLimitPartition<string> GlobalPartition(HttpContext context)
    {
        if (ActorOf(context) is { } userId)
        {
            return SignedInLimit.Partition($"sub:{userId}");
        }

        var policy = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        return policy is not null && AnonymousAuthPolicies.Contains(policy)
            ? RateLimitPartition.GetNoLimiter(string.Empty)
            : AnonymousLimit.Partition($"ip:{IpKey(context)}");
    }

    internal static async ValueTask RejectAsync(
        OnRejectedContext context, RateLimit limit, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var services = http.RequestServices;
        services.GetRequiredService<ILoggerFactory>().CreateLogger(AuthLog.Category)
            .RateLimited(limit.Policy, limit.CountsBy == CountsBy.Sub ? ActorOf(http) : null);
        services.GetRequiredService<AuthMeter>().RateLimited(limit.Policy);

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var given) ? given : limit.Segment;
        http.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        await http.Response.WriteAsJsonAsync(
            ApiResponse<object>.ErrorResponse(
                TryLater,
                StatusCodes.Status429TooManyRequests,
                [new ApiError(null, ErrorCodes.TooManyRequests, TryLater)]),
            cancellationToken);
    }

    internal static string? PartitionKey(HttpContext context, CountsBy countsBy) =>
        countsBy == CountsBy.Sub ? ActorOf(context)?.ToString() : IpKey(context);

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

internal sealed class RateLimitPolicy(RateLimit limit) : IRateLimiterPolicy<string>
{
    private static readonly RateLimitPartition<string> AlreadyCountedAsAnonymous =
        RateLimitPartition.GetNoLimiter(string.Empty);

    public Func<OnRejectedContext, CancellationToken, ValueTask> OnRejected =>
        (context, cancellationToken) => RateLimits.RejectAsync(context, limit, cancellationToken);

    public RateLimitPartition<string> GetPartition(HttpContext httpContext) =>
        RateLimits.PartitionKey(httpContext, limit.CountsBy) is { } partitionKey
            ? limit.Partition(partitionKey)
            : AlreadyCountedAsAnonymous;
}
