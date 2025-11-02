using System.IO;

namespace ProjectManagement.Services.DocRepo;

public interface IFileScanner
{
    Task ScanOrThrowAsync(Stream fileStream, CancellationToken cancellationToken = default);
}

public sealed class NoopFileScanner : IFileScanner
{
    public Task ScanOrThrowAsync(Stream fileStream, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
