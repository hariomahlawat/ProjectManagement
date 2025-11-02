using System.IO;

namespace ProjectManagement.Services.DocRepo;

public interface IOcrEngine
{
    Task<string> ExtractAsync(Stream pdfStream, CancellationToken cancellationToken = default);
}

public sealed class NoopOcrEngine : IOcrEngine
{
    public Task<string> ExtractAsync(Stream pdfStream, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Empty);
}
