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
