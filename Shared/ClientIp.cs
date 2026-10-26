using System.Net;

namespace Laneway.Api;

public static class ClientIp
{
    public const string Field = "clientIp";

    public static string? Of(HttpContext? context) => AddressOf(context)?.ToString();

    public static IPAddress? AddressOf(HttpContext? context) => context?.Connection.RemoteIpAddress switch
    {
        { IsIPv4MappedToIPv6: true } address => address.MapToIPv4(),
        var address => address
    };
}
