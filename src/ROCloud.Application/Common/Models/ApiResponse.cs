namespace ROCloud.Application.Common.Models;

/// <summary>Standard success envelope for API responses. Errors are returned by ExceptionMiddleware.</summary>
public sealed record ApiResponse<T>(bool Success, T? Data, string? Error = null, string? Code = null)
{
    public static ApiResponse<T> Ok(T data) => new(true, data);
    public static ApiResponse<T> Fail(string error, string? code = null) => new(false, default, error, code);
}
