using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Application.Security;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Storage;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class FileSystemArppAttachmentStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arpp-storage-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAsync_ValidatesSignature_ScansAndStoresWithinArppRoot()
    {
        Directory.CreateDirectory(_root);
        var validator = new FakeSecurityValidator();
        var storage = new FileSystemArppAttachmentStorage(
            new FakeUploadRootProvider(_root),
            validator,
            Options.Create(new ArppAttachmentOptions
            {
                StorageFolderName = "arpp-tests",
                MaxFileSizeBytes = 1024 * 1024
            }),
            NullLogger<FileSystemArppAttachmentStorage>.Instance);
        var bytes = "%PDF-1.7\n%%EOF"u8.ToArray();

        var result = await storage.SaveAsync(
            42,
            "issued-document.pdf",
            "application/pdf",
            bytes.Length,
            new MemoryStream(bytes));

        Assert.StartsWith("arpp-tests/42/", result.StorageKey);
        Assert.Equal(64, result.Sha256.Length);
        Assert.Equal(1, validator.ScanCount);
        var stored = await storage.OpenReadAsync(result.StorageKey);
        Assert.NotNull(stored);
        await using var verifiedStored = stored!;
        Assert.Equal(bytes.Length, verifiedStored.Length);
    }

    [Fact]
    public async Task SaveAsync_RejectsFileWithoutPdfSignature()
    {
        Directory.CreateDirectory(_root);
        var storage = new FileSystemArppAttachmentStorage(
            new FakeUploadRootProvider(_root),
            new FakeSecurityValidator(),
            Options.Create(new ArppAttachmentOptions()),
            NullLogger<FileSystemArppAttachmentStorage>.Instance);
        var bytes = "not a PDF"u8.ToArray();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => storage.SaveAsync(
            1,
            "fake.pdf",
            "application/pdf",
            bytes.Length,
            new MemoryStream(bytes)));

        Assert.Contains("pdf signature", exception.Message.ToLowerInvariant());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }

    private sealed class FakeSecurityValidator : IFileSecurityValidator
    {
        public int ScanCount { get; private set; }

        public void ValidateRelativePath(string relativePath)
        {
            if (Path.IsPathRooted(relativePath) || relativePath.Split(new[] { '/', '\\' }).Any(segment => segment == ".."))
            {
                throw new ArgumentException("Unsafe path.", nameof(relativePath));
            }
        }

        public Task<bool> IsSafeAsync(
            string filePath,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            ScanCount++;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeUploadRootProvider : IUploadRootProvider
    {
        public FakeUploadRootProvider(string rootPath) => RootPath = rootPath;
        public string RootPath { get; }
        public string GetProjectRoot(int projectId) => Path.Combine(RootPath, "projects", projectId.ToString());
        public string GetProjectPhotosRoot(int projectId) => Path.Combine(GetProjectRoot(projectId), "photos");
        public string GetProjectDocumentsRoot(int projectId) => Path.Combine(GetProjectRoot(projectId), "documents");
        public string GetProjectCommentsRoot(int projectId) => Path.Combine(GetProjectRoot(projectId), "comments");
        public string GetProjectVideosRoot(int projectId) => Path.Combine(GetProjectRoot(projectId), "videos");
        public string GetSocialMediaRoot(string storagePrefix, Guid eventId) => Path.Combine(RootPath, "social", eventId.ToString("N"));
    }
}
