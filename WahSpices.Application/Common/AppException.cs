namespace WahSpices.Application.Common;

public class AppException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }
    public string? Field { get; }

    public AppException(string message, int statusCode = 400, string errorCode = "BAD_REQUEST", string? field = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Field = field;
    }

    public static AppException NotFound(string resource) =>
        new($"{resource} not found.", 404, "NOT_FOUND");

    public static AppException Unauthorized(string message = "Unauthorized access.") =>
        new(message, 401, "UNAUTHORIZED");

    public static AppException Forbidden(string message = "Access denied.") =>
        new(message, 403, "FORBIDDEN");

    public static AppException Conflict(string message) =>
        new(message, 409, "CONFLICT");

    public static AppException Validation(string message, string? field = null) =>
        new(message, 422, "VALIDATION_ERROR", field);
}
