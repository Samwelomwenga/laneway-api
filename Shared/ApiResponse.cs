
namespace DefaultNamespace;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public int StatusCode { get; set; }
    public List<string> Errors { get; set; } = new();

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

    public static ApiResponse<T> ErrorResponse(string message, int statusCode = 400, List<string>? errors = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            StatusCode = statusCode,
            Errors = errors ?? new List<string>()
        };
    }
}

public  class PagedResponse<T> : ApiResponse<List<T>>
{
    public new List<T> Data { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageSize { get; set; }
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    
    public static PagedResponse<T> SuccessResponse(List<T> data, int totalCount, int pageSize, int currentPage, string message = "Request was successful", int statusCode = 200)
    {
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        return new PagedResponse<T>
        {
            Success = true,
            Message = message,
            Data = data,
            StatusCode = statusCode,
            TotalCount = totalCount,
            PageSize = pageSize,
            CurrentPage = currentPage,
            TotalPages = totalPages
        };
    }
    
    public static new PagedResponse<T> ErrorResponse(string message, int statusCode = 400, List<string>? errors = null)
    {
        return new PagedResponse<T>
        {
            Success = false,
            Message = message,
            StatusCode = statusCode,
            Errors = errors ?? new List<string>(),
            Data = new List<T>(),
            TotalCount = 0,
            PageSize = 0,
            CurrentPage = 0,
            TotalPages = 0
        };
    }
    
    public bool HasNextPage => CurrentPage < TotalPages;
    public bool HasPreviousPage => CurrentPage > 1;
}
