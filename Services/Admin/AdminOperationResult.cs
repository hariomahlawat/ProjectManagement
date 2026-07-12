namespace ProjectManagement.Services.Admin;

public record AdminOperationResult(
    bool Succeeded,
    string? UserMessage = null,
    string? ErrorCode = null,
    string? TraceId = null)
{
    public static AdminOperationResult Success(string? message = null) =>
        new(true, message);

    public static AdminOperationResult Failure(
        string message,
        string? errorCode = null,
        string? traceId = null) =>
        new(false, message, errorCode, traceId);
}

public sealed record AdminOperationResult<T>(
    bool Succeeded,
    T? Value = default,
    string? UserMessage = null,
    string? ErrorCode = null,
    string? TraceId = null)
{
    public static AdminOperationResult<T> Success(T value, string? message = null) =>
        new(true, value, message);

    public static AdminOperationResult<T> Failure(
        string message,
        string? errorCode = null,
        string? traceId = null) =>
        new(false, default, message, errorCode, traceId);
}
