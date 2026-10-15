using System.Text.Json.Serialization;

namespace Laneway.Api;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public int StatusCode { get; set; }
    public List<ApiError> Errors { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceId { get; set; }

    public static ApiResponse<T> SuccessResponse(T data, string message = "Request was successful", int statusCode = 200)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Message = message,
            Data = data,
            StatusCode = statusCode
        };
    }

    public static ApiResponse<T> ErrorResponse(
        string message, int statusCode = 400, List<ApiError>? errors = null, string? traceId = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            StatusCode = statusCode,
            Errors = errors ?? new List<ApiError>(),
            TraceId = traceId
        };
    }
}

public class PagedResponse<T> : ApiResponse<List<T>>
{
    public new List<T> Data { get; set; } = new();
    public Pagination Pagination { get; set; } = null!;

    public static PagedResponse<T> SuccessResponse(List<T> data, int totalCount, int pageSize, int pageNumber, string message = "Request was successful", int statusCode = 200)
    {
        return new PagedResponse<T>
        {
            Success = true,
            Message = message,
            Data = data,
            StatusCode = statusCode,
            Pagination = Pagination.Of(pageNumber, pageSize, totalCount)
        };
    }
}

public record Pagination(
    int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasNextPage, bool HasPreviousPage)
{
    public static Pagination Of(int pageNumber, int pageSize, int totalCount)
    {
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        return new Pagination(
            pageNumber, pageSize, totalCount, totalPages, pageNumber < totalPages, pageNumber > 1);
    }
}
