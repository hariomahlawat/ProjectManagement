using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

[Collection("Upload root environment")]
public sealed class UploadRootProviderTests : IDisposable
{
    private readonly string? _previousUploadRoot = Environment.GetEnvironmentVariable("PM_UPLOAD_ROOT");

    public UploadRootProviderTests()
        => Environment.SetEnvironmentVariable("PM_UPLOAD_ROOT", null);

    public void Dispose()
        => Environment.SetEnvironmentVariable("PM_UPLOAD_ROOT", _previousUploadRoot);

    [Fact]
    public void UsesFallbackWhenPrimaryCreationFails()
    {
        var primaryRoot = Path.Combine(Path.GetTempPath(), "pm-restricted");
        var options = Options.Create(new ProjectPhotoOptions
        {
            StorageRoot = primaryRoot
        });

        var documentOptions = Options.Create(new ProjectDocumentOptions());
        var environment = new TestWebHostEnvironment
        {
            ContentRootPath = Path.Combine(Path.GetTempPath(), "pm-content-root")
        };

        var attempted = new List<string>();
        DirectoryInfo Factory(string path)
        {
            attempted.Add(path);

            if (string.Equals(path, Path.GetFullPath(primaryRoot), StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("restricted");
            }

            // Record only: the fallback can be the real per-user folder on Windows.
            return new DirectoryInfo(path);
        }

        var provider = new UploadRootProvider(
            options,
            documentOptions,
            environment,
            NullLogger<UploadRootProvider>.Instance,
            Factory);

        // The fallback is per-user LocalAppData on Windows and the content root elsewhere
        // (UploadRootProvider.GetFallbackRoot).
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var expectedFallback = OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(localAppData)
            ? Path.GetFullPath(Path.Combine(localAppData, "ProjectManagement", "uploads"))
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, "uploads"));

        Assert.Equal(expectedFallback, provider.RootPath);
        Assert.Equal(Path.GetFullPath(primaryRoot), attempted[0]);
        Assert.Contains(expectedFallback, attempted);
    }

    [Fact]
    public void CreatesVideoDirectoryUnderProject()
    {
        var options = Options.Create(new ProjectPhotoOptions());
        var documentOptions = Options.Create(new ProjectDocumentOptions
        {
            ProjectsSubpath = "projects",
            VideosSubpath = "videos"
        });

        var environment = new TestWebHostEnvironment
        {
            ContentRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        };

        var provider = new UploadRootProvider(
            options,
            documentOptions,
            environment,
            NullLogger<UploadRootProvider>.Instance);

        var projectRoot = provider.GetProjectRoot(42);
        var videosRoot = provider.GetProjectVideosRoot(42);

        Assert.True(Directory.Exists(videosRoot));
        Assert.StartsWith(projectRoot, videosRoot, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(projectRoot, "videos"), videosRoot);
    }

    [Fact]
    public void ResolvesSocialMediaRootUnderUploadRoot()
    {
        var options = Options.Create(new ProjectPhotoOptions());
        var documentOptions = Options.Create(new ProjectDocumentOptions());
        var environment = new TestWebHostEnvironment
        {
            ContentRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        };

        var provider = new UploadRootProvider(
            options,
            documentOptions,
            environment,
            NullLogger<UploadRootProvider>.Instance);

        var eventId = Guid.NewGuid();
        var socialRoot = provider.GetSocialMediaRoot("org/social/{eventId}", eventId);

        Assert.True(Directory.Exists(socialRoot));
        Assert.StartsWith(provider.RootPath, socialRoot);
        Assert.EndsWith(Path.Combine("org", "social", eventId.ToString("D")), socialRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSocialMediaRootThrowsWhenTokenMissing()
    {
        var options = Options.Create(new ProjectPhotoOptions());
        var documentOptions = Options.Create(new ProjectDocumentOptions());
        var environment = new TestWebHostEnvironment
        {
            ContentRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        };

        var provider = new UploadRootProvider(
            options,
            documentOptions,
            environment,
            NullLogger<UploadRootProvider>.Instance);

        var eventId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => provider.GetSocialMediaRoot("org/social", eventId));
    }
}
