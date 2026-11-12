namespace ProjectManagement.Features.MediaLibrary.Services;

public class MediaProcessingPermanentException : Exception
{
    public MediaProcessingPermanentException(string message) : base(message) { }
    public MediaProcessingPermanentException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class MediaContentUnavailableException : MediaProcessingPermanentException
{
    public MediaContentUnavailableException(string message) : base(message) { }
    public MediaContentUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}


/// <summary>
/// Raised when a catalogue refresh or human decision supersedes an in-flight result.
/// It is intentionally recoverable so the owning job is retried against the latest state.
/// </summary>
public sealed class MediaProcessingSupersededException : Exception
{
    public MediaProcessingSupersededException(string message) : base(message) { }
    public MediaProcessingSupersededException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Raised in place of a "source missing" failure when the storage root that holds the
/// asset (an external share or the PRISM upload root) is itself unreachable. A missing
/// root is an outage, not proof that the file was removed, so the failure is recoverable
/// and the asset must stay visible.
/// </summary>
public sealed class MediaSourceStorageUnreachableException : IOException
{
    public MediaSourceStorageUnreachableException(string message) : base(message) { }
}
