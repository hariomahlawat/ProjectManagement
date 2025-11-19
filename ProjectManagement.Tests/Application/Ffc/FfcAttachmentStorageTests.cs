using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Ffc;
using ProjectManagement.Application.Security;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using Microsoft.Extensions.Options;
using Xunit;

namespace ProjectManagement.Tests.Application.Ffc;

public sealed class FfcAttachmentStorageTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly TestFileSecurityValidator _validator = new();
    private readonly TestWebHostEnvironment _environment;
    private readonly string _uploadsRoot;

    public FfcAttachmentStorageTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ffc-attachments-{Guid.NewGuid():N}")
            .Options;

        _db = new ApplicationDbContext(options);
        _environment = new TestWebHostEnvironment
        {
            ContentRootPath = Path.Combine(Path.GetTempPath(), "pm-ffc-tests", Guid.NewGuid().ToString("N"))
        };

        Directory.CreateDirectory(_environment.ContentRootPath);
        _uploadsRoot = Path.Combine(_environment.ContentRootPath, "uploads");
        Directory.CreateDirectory(_uploadsRoot);
    }

    [Fact]
    public async Task SaveAsync_ReturnsError_WhenUserNotAuthorised()
    {
        var storage = CreateStorage(new StubUserContext(isAdmin: false, isHoD: false));
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        var formFile = new FormFile(stream, 0, stream.Length, "upload", "test.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var result = await storage.SaveAsync(123, formFile, FfcAttachmentKind.Pdf, "Test");

        Assert.False(result.Success);
        Assert.Equal("Only Admin or HoD roles can manage attachments.", result.ErrorMessage);
        Assert.Null(result.Attachment);
        Assert.Equal(0, await _db.FfcAttachments.CountAsync());
    }

    [Fact]
    public async Task SaveAsync_ReturnsError_WhenFileTooLarge()
    {
        var options = new FfcAttachmentOptions { MaxFileSizeBytes = 1 * 1024 * 1024 };
        var storage = CreateStorage(new StubUserContext(isAdmin: true, isHoD: false), options);

        var oversized = new byte[checked((int)(options.MaxFileSizeBytes + 1))];
        await using var stream = new MemoryStream(oversized);

        var formFile = new FormFile(stream, 0, stream.Length, "upload", "test.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var result = await storage.SaveAsync(123, formFile, FfcAttachmentKind.Pdf, "Test");

        Assert.False(result.Success);
        Assert.Equal("File exceeds maximum size of 1 MB.", result.ErrorMessage);
        Assert.Null(result.Attachment);
        Assert.Equal(0, await _db.FfcAttachments.CountAsync());
        Assert.Equal(0, _validator.CallCount);
    }

    [Fact]
    public async Task SaveAsync_ReturnsError_WhenContentTypeInvalid()
    {
        var storage = CreateStorage(new StubUserContext(isAdmin: true, isHoD: false));

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var formFile = new FormFile(stream, 0, stream.Length, "upload", "note.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        var result = await storage.SaveAsync(42, formFile, FfcAttachmentKind.Pdf, null);

        Assert.False(result.Success);
        Assert.Equal("Only PDF/JPEG/PNG/WEBP allowed.", result.ErrorMessage);
        Assert.Null(result.Attachment);
        Assert.Equal(0, await _db.FfcAttachments.CountAsync());
        Assert.Equal(0, _validator.CallCount);
    }

    [Fact]
    public async Task DeleteAsync_Throws_WhenUserNotAuthorised()
    {
        var options = new FfcAttachmentOptions();
        var storage = CreateStorage(new StubUserContext(isAdmin: false, isHoD: false), options);
        var filePath = Path.Combine(GetResolvedStorageRoot(options), "existing.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllBytesAsync(filePath, new byte[] { 1, 2, 3 });

        var attachment = new FfcAttachment
        {
            FfcRecordId = 1,
            Kind = FfcAttachmentKind.Pdf,
            FilePath = filePath,
            ContentType = "application/pdf",
            SizeBytes = 3,
            UploadedAt = DateTimeOffset.UtcNow
        };

        _db.FfcAttachments.Add(attachment);
        await _db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<FfcAttachmentAuthorizationException>(() => storage.DeleteAsync(attachment));

        Assert.Equal("Only Admin or HoD roles can manage attachments.", exception.Message);
        Assert.True(File.Exists(filePath));
        Assert.Equal(1, await _db.FfcAttachments.CountAsync());
    }

    private FfcAttachmentStorage CreateStorage(IUserContext userContext, FfcAttachmentOptions? options = null)
    {
        var effectiveOptions = options ?? new FfcAttachmentOptions();
        var resolvedRoot = GetResolvedStorageRoot(effectiveOptions);
        effectiveOptions.StorageRoot = resolvedRoot;

        var uploadRootProvider = new TestUploadRootProvider(_uploadsRoot);
        var pathResolver = new UploadPathResolver(uploadRootProvider);

        return new FfcAttachmentStorage(
            _db,
            _validator,
            uploadRootProvider,
            pathResolver,
            userContext,
            Options.Create(effectiveOptions));
    }

    public void Dispose()
    {
        _db.Dispose();

        if (Directory.Exists(_environment.ContentRootPath))
        {
            Directory.Delete(_environment.ContentRootPath, recursive: true);
        }
    }

    private sealed class TestFileSecurityValidator : IFileSecurityValidator
    {
        public int CallCount { get; private set; }

        public Task<bool> IsSafeAsync(string filePath, string contentType, System.Threading.CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(true);
        }
    }

    private string GetResolvedStorageRoot(FfcAttachmentOptions options)
    {
        var folderName = string.IsNullOrWhiteSpace(options.StorageFolderName)
            ? "ffc"
            : options.StorageFolderName.Trim().Trim('/', '\\');

        if (!string.IsNullOrWhiteSpace(options.StorageRoot))
        {
            return Path.IsPathRooted(options.StorageRoot)
                ? options.StorageRoot
                : Path.Combine(_uploadsRoot, options.StorageRoot);
        }

        return Path.Combine(_uploadsRoot, folderName);
    }

    private sealed class TestUploadRootProvider(string rootPath) : IUploadRootProvider
    {
        public string RootPath { get; } = rootPath;

        public string GetProjectRoot(int projectId) => throw new NotSupportedException();

        public string GetProjectPhotosRoot(int projectId) => throw new NotSupportedException();

        public string GetProjectDocumentsRoot(int projectId) => throw new NotSupportedException();

        public string GetProjectCommentsRoot(int projectId) => throw new NotSupportedException();

        public string GetProjectVideosRoot(int projectId) => throw new NotSupportedException();

        public string GetSocialMediaRoot(string storagePrefix, Guid eventId) => throw new NotSupportedException();
    }

    private sealed class StubUserContext : IUserContext
    {
        public StubUserContext(bool isAdmin, bool isHoD)
        {
            var identity = new ClaimsIdentity();

            if (isAdmin)
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
            }

            if (isHoD)
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, "HoD"));
            }

            User = new ClaimsPrincipal(identity);
        }

        public ClaimsPrincipal User { get; }

        public string? UserId => null;
    }
}
