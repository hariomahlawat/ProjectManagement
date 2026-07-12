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
