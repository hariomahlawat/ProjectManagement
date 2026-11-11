using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Centralises media-processing failure classification so workers, administration
/// actions and synchronisation logic apply the same retry policy.
/// </summary>
public static class MediaProcessingFailurePolicy
{
    public const string SourceUnavailableMarker = "[SourceUnavailable] ";

    /// <summary>
    /// Recorded when every attempt at a job ended with the worker's lease expiring (a hang,
    /// or an image that brought the process down). It is treated as permanent so neither
    /// the bulk retry nor automatic catalogue requeues run it again; an administrator must
    /// force a retry after checking the source.
    /// </summary>
    public const string RepeatedWorkerLossCode = "RepeatedWorkerLoss";

    public static readonly string[] PermanentFailureCodeNames =
    {
        nameof(MediaContentUnavailableException),
        nameof(MediaProcessingPermanentException),
        nameof(FileNotFoundException),
        nameof(DirectoryNotFoundException),
        nameof(NotSupportedException),
        nameof(InvalidDataException),
        RepeatedWorkerLossCode
    };

    public static readonly string[] RecoverableFailureCodeNames =
    {
        nameof(IOException),
        nameof(ObjectDisposedException),
        nameof(TimeoutException),
        nameof(UnauthorizedAccessException),
        nameof(MediaProcessingSupersededException),
        nameof(MediaSourceStorageUnreachableException),
        "ExpiredWorkerLock",
        "WorkerStopping"
    };

    private static readonly HashSet<string> PermanentFailureCodes =
        new(PermanentFailureCodeNames, StringComparer.Ordinal);

    public static bool IsPermanent(Exception exception)
        => exception is MediaProcessingPermanentException
            or FileNotFoundException
            or DirectoryNotFoundException
            or NotSupportedException
            or InvalidDataException;

    public static bool IsSourceUnavailable(Exception exception)
        => exception is MediaContentUnavailableException
            or FileNotFoundException
            or DirectoryNotFoundException;

    public static bool IsRecoverableFailureCode(string? failureCode)
        => !string.IsNullOrWhiteSpace(failureCode)
           && !PermanentFailureCodes.Contains(failureCode);

    public static bool IsPermanentFailureCode(string? failureCode)
        => !string.IsNullOrWhiteSpace(failureCode)
           && PermanentFailureCodes.Contains(failureCode);

    /// <summary>
    /// True for a job dead-lettered because it repeatedly lost its worker. Automatic
    /// requeues (catalogue synchronisation, classifier refresh) must leave it alone unless
    /// the source content itself has changed.
    /// </summary>
    public static bool IsHeldForAdministrator(MediaProcessingJob job)
        => job.Status == MediaProcessingJobStatus.DeadLetter
           && string.Equals(job.FailureCode, RepeatedWorkerLossCode, StringComparison.Ordinal);

    public static string MarkSourceUnavailable(string message)
        => SourceUnavailableMarker + message;

    public static bool HasSourceUnavailableMarker(string? value)
        => value?.StartsWith(SourceUnavailableMarker, StringComparison.Ordinal) == true;

    public static string GetSourceUnavailableMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "The source media is unavailable.";
        return HasSourceUnavailableMarker(value)
            ? value[SourceUnavailableMarker.Length..].Trim()
            : value.Trim();
    }
}
