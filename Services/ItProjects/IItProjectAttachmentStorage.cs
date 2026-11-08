using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.ItProjects;

/// <summary>Where IT project attachment bytes live. Keys are relative to the upload root.</summary>
public interface IItProjectAttachmentStorage
{
    /// <summary>Stores the content and returns its relative storage key. Throws <see cref="ItProjectAttachmentRejectedException"/> when the configured security check fails.</summary>
    Task<string> SaveAsync(int itProjectId, string originalFileName, Stream content, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}

/// <summary>Raised by the storage when the stored bytes fail the configured security checks.</summary>
public sealed class ItProjectAttachmentRejectedException : System.Exception
{
    public ItProjectAttachmentRejectedException(string message) : base(message)
    {
    }
}
