using System.Net.Http.Json;
using System.Text.Json;

namespace Laneway.Api.Tests;

public sealed record ApiEnvelope<T>(bool Success, string Message, T? Data, int StatusCode, List<ApiError>? Errors);

public sealed record ApiPage<T>(
    bool Success,
    string Message,
    List<T>? Data,
    int StatusCode,
    List<ApiError>? Errors,
    int TotalCount,
    int PageSize,
    int CurrentPage,
    int TotalPages)
{
    public List<T> Entries => Data ?? [];
}

public static class Replies
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new EnumNameConverter(), new UtcDateTimeConverter() }
    };

    public static async Task<ApiEnvelope<T>> ReadEnvelope<T>(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(Options))!;
    }

    public static async Task<ApiPage<T>> ReadPage<T>(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<ApiPage<T>>(Options))!;
    }

    public static async Task<List<ApiError>> ReadErrors(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<ApiEnvelope<object>>(Options))!.Errors ?? [];
    }
}
