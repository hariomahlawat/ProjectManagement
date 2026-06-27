using System.Collections.Concurrent;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record MediaLibraryDiagnosticEvent(
    MediaLibraryQueryOperation Operation,
    bool Succeeded,
    string Reference,
    string Message,
    DateTimeOffset OccurredAtUtc,
    long DurationMilliseconds);

public interface IMediaLibraryDiagnostics
{
    MediaLibraryDiagnosticEvent RecordSuccess(MediaLibraryQueryOperation operation, long durationMilliseconds);
    MediaLibraryDiagnosticEvent RecordFailure(MediaLibraryQueryOperation operation, Exception exception, long durationMilliseconds);
    IReadOnlyList<MediaLibraryDiagnosticEvent> GetLatest();
}

public sealed class MediaLibraryDiagnostics : IMediaLibraryDiagnostics
{
    private readonly ConcurrentDictionary<MediaLibraryQueryOperation, MediaLibraryDiagnosticEvent> _latest = new();

    public MediaLibraryDiagnosticEvent RecordSuccess(MediaLibraryQueryOperation operation, long durationMilliseconds)
    {
        var value = new MediaLibraryDiagnosticEvent(
            operation,
            true,
            CreateReference(operation),
            "Healthy",
            DateTimeOffset.UtcNow,
            Math.Max(0, durationMilliseconds));
        _latest[operation] = value;
        return value;
    }

    public MediaLibraryDiagnosticEvent RecordFailure(
        MediaLibraryQueryOperation operation,
        Exception exception,
        long durationMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var value = new MediaLibraryDiagnosticEvent(
            operation,
            false,
            CreateReference(operation),
            ToSafeMessage(exception),
            DateTimeOffset.UtcNow,
            Math.Max(0, durationMilliseconds));
        _latest[operation] = value;
        return value;
    }

    public IReadOnlyList<MediaLibraryDiagnosticEvent> GetLatest()
        => _latest.Values
            .OrderBy(item => item.Operation)
            .ToArray();

    private static string CreateReference(MediaLibraryQueryOperation operation)
    {
        var operationName = operation.ToString();
        var prefix = operationName[..Math.Min(3, operationName.Length)].ToUpperInvariant();
        var value = $"MLQ-{prefix}-{Guid.NewGuid():N}";
        return value[..Math.Min(18, value.Length)];
    }

    private static string ToSafeMessage(Exception exception)
    {
        var baseException = exception.GetBaseException();
        return baseException switch
        {
            TimeoutException => "The database operation timed out.",
            _ => "The catalogue operation could not be completed. Review application logs using the reference shown."
        };
    }
}
