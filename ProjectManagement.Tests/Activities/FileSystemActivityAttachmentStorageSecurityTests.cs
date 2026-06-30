using ProjectManagement.Application.Security;
using ProjectManagement.Services.Activities;
using FakeUploadRootProvider = ProjectManagement.Tests.Fakes.TestUploadRootProvider;

namespace ProjectManagement.Tests.Activities;

public sealed class FileSystemActivityAttachmentStorageSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "prism-activity-storage-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OpenReadAsync_RejectsTraversalOutsideActivitiesRoot()
    {
        Directory.CreateDirectory(Path.Combine(_root, "activities"));
        Directory.CreateDirectory(Path.Combine(_root, "other-module"));
        await File.WriteAllTextAsync(
            Path.Combine(_root, "other-module", "private.txt"),
            "private");

        var storage = new FileSystemActivityAttachmentStorage(
            new FakeUploadRootProvider(_root),
            new PassFileSecurityValidator());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.OpenReadAsync("activities/../other-module/private.txt"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class PassFileSecurityValidator : IFileSecurityValidator
    {
        public void ValidateRelativePath(string relativePath)
        {
        }

        public Task<bool> IsSafeAsync(
            string filePath,
            string contentType,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
