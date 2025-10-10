using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectVideoServiceTests
{
    [Fact]
    public async Task AddAsync_ThrowsWhenFileTooLarge()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 11);

        using var stream = CreateVideoStream(512);
        var options = CreateOptions();
        options.MaxFileSizeBytes = 128;

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var service = CreateService(db, options);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddAsync(11,
                    stream,
                    "oversized.mp4",
                    "video/mp4",
                    "user-1",
                    title: null,
                    description: null,
                    totId: null,
                    setAsFeatured: false,
                    CancellationToken.None));
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task AddAsync_SetsFeaturedWhenFirstVideoUploaded()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 17);

        var options = CreateOptions();
        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var service = CreateService(db, options);

            using var stream = CreateVideoStream();
            var video = await service.AddAsync(17,
                stream,
                "launch.mp4",
                "video/mp4",
                "owner",
                title: "Launch",
                description: null,
                totId: null,
                setAsFeatured: false,
                CancellationToken.None);

            var project = await db.Projects.Include(p => p.Videos).SingleAsync(p => p.Id == 17);

            Assert.Equal(video.Id, project.FeaturedVideoId);
            Assert.Equal(video.Version, project.FeaturedVideoVersion);
            Assert.True(project.Videos.Single().IsFeatured);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task SetFeaturedAsync_TogglesFeaturedVideo()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 23);

        var options = CreateOptions();
        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var service = CreateService(db, options);

            using var firstStream = CreateVideoStream(300);
            var first = await service.AddAsync(23,
                firstStream,
                "alpha.mp4",
                "video/mp4",
                "owner",
                title: "Alpha",
                description: null,
                totId: null,
                setAsFeatured: false,
                CancellationToken.None);

            using var secondStream = CreateVideoStream(256);
            var second = await service.AddAsync(23,
                secondStream,
                "beta.mp4",
                "video/mp4",
                "owner",
                title: "Beta",
                description: null,
                totId: null,
                setAsFeatured: false,
                CancellationToken.None);

            var updated = await service.SetFeaturedAsync(23, second.Id, true, "owner", CancellationToken.None);
            Assert.NotNull(updated);
            Assert.True(updated!.IsFeatured);

            var project = await db.Projects.Include(p => p.Videos).SingleAsync(p => p.Id == 23);
            Assert.Equal(second.Id, project.FeaturedVideoId);
            Assert.False(project.Videos.Single(v => v.Id == first.Id).IsFeatured);
            Assert.True(project.Videos.Single(v => v.Id == second.Id).IsFeatured);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task RemoveAsync_DeletesVideoFilesAndClearsFeatured()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 31);

        var options = CreateOptions();
        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var service = CreateService(db, options);

            using var stream = CreateVideoStream();
            var video = await service.AddAsync(31,
                stream,
                "demo.mp4",
                "video/mp4",
                "owner",
                title: null,
                description: null,
                totId: null,
                setAsFeatured: true,
                CancellationToken.None);

            var videoPath = Path.Combine(root, "projects", "31", "videos", video.StorageKey + ".mp4");
            Assert.True(File.Exists(videoPath));

            var removed = await service.RemoveAsync(31, video.Id, "owner", CancellationToken.None);
            Assert.True(removed);
            Assert.False(File.Exists(videoPath));

            var project = await db.Projects.SingleAsync(p => p.Id == 31);
            Assert.Null(project.FeaturedVideoId);
            Assert.Equal(0, project.FeaturedVideoVersion);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task OpenOriginalAsync_ReturnsStreamWhenFileExists()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 41);

        var options = CreateOptions();
        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var service = CreateService(db, options);

            using var stream = CreateVideoStream();
            var video = await service.AddAsync(41,
                stream,
                "walkthrough.mp4",
                "video/mp4",
                "owner",
                title: null,
                description: null,
                totId: null,
                setAsFeatured: false,
                CancellationToken.None);

            var original = await service.OpenOriginalAsync(41, video.Id, CancellationToken.None);
            Assert.NotNull(original);
            await using (original!.Value.Stream)
            {
                Assert.Equal("video/mp4", original.Value.ContentType);
                Assert.True(original.Value.Stream.Length > 0);
            }
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task OpenOriginalAsync_ReturnsNullWhenFileMissing()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 47);

        var options = CreateOptions();
        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var service = CreateService(db, options);

            using var stream = CreateVideoStream();
            var video = await service.AddAsync(47,
                stream,
                "tour.mp4",
                "video/mp4",
                "owner",
                title: null,
                description: null,
                totId: null,
                setAsFeatured: false,
                CancellationToken.None);

            var videoPath = Path.Combine(root, "projects", "47", "videos", video.StorageKey + ".mp4");
            File.Delete(videoPath);

            var original = await service.OpenOriginalAsync(47, video.Id, CancellationToken.None);
            Assert.Null(original);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task SeedProjectAsync(ApplicationDbContext db, int projectId)
    {
        db.Projects.Add(new Project
        {
            Id = projectId,
            Name = $"Project {projectId}",
            CreatedByUserId = "creator",
            RowVersion = new byte[] { 1 }
        });

        await db.SaveChangesAsync();
    }

    private static ProjectVideoService CreateService(ApplicationDbContext db, ProjectVideoOptions options)
    {
        var clock = new FixedClock(new DateTimeOffset(2024, 02, 01, 0, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var optionsWrapper = Options.Create(options);
        var photoOptions = Options.Create(new ProjectPhotoOptions());
        var documentOptions = Options.Create(new ProjectDocumentOptions());
        var environment = new TestWebHostEnvironment
        {
            ContentRootPath = Path.Combine(Path.GetTempPath(), "pm-video-tests-env")
        };
        var uploadRoot = new UploadRootProvider(photoOptions, documentOptions, environment, NullLogger<UploadRootProvider>.Instance);
        return new ProjectVideoService(db, clock, audit, uploadRoot, optionsWrapper, NullLogger<ProjectVideoService>.Instance);
    }

    private static ProjectVideoOptions CreateOptions()
    {
        return new ProjectVideoOptions
        {
            AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "video/mp4",
                "video/webm"
            },
            MaxFileSizeBytes = 25 * 1024 * 1024
        };
    }

    private static MemoryStream CreateVideoStream(int length = 256)
    {
        var buffer = new byte[length];
        new Random(42).NextBytes(buffer);
        return new MemoryStream(buffer);
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "pm-videos-tests", Guid.NewGuid().ToString("N"));
    }

    private static void CleanupTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void SetUploadRoot(string root)
    {
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("PM_UPLOAD_ROOT", root);
    }

    private static void ResetUploadRoot()
    {
        Environment.SetEnvironmentVariable("PM_UPLOAD_ROOT", null);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now)
        {
            UtcNow = now;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
