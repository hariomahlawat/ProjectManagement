using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Tests.Fakes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectPhotoServiceTests
{
    [Fact]
    public async Task AddAsync_ThrowsWhenFileTooLarge()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 1);

        using var oversized = new MemoryStream(new byte[512]);
        var options = CreateOptions();
        options.MaxFileSizeBytes = 128;

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var service = CreateService(db, options);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddAsync(1, oversized, "large.png", "image/png", "user-1", false, null, CancellationToken.None));
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task AddAsync_ThrowsWhenFormatNotAllowed()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 5);

        await using var stream = await CreateImageStreamAsync(800, 600, saveAsBmp: true);

        var options = CreateOptions();
        options.AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/png"
        };

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var service = CreateService(db, options);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddAsync(5, stream, "sample.bmp", "image/bmp", "auditor", false, null, CancellationToken.None));

            Assert.Contains("not allowed", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task AddAsync_ThrowsWhenImageTooSmall()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 7);

        await using var stream = await CreateImageStreamAsync(320, 240);

        var options = CreateOptions();
        options.MinWidth = 720;
        options.MinHeight = 540;

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var service = CreateService(db, options);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddAsync(7, stream, "small.png", "image/png", "editor", false, null, CancellationToken.None));

            Assert.Contains("at least", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task AddAsync_WithCropGeneratesDerivativesAndSetsCover()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 11);

        await using var stream = await CreateImageStreamAsync(1600, 1200);

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var options = CreateOptions();
            var service = CreateService(db, options);

            var crop = new ProjectPhotoCrop(100, 150, 1200, 900);
            var photo = await service.AddAsync(11, stream, "cover.png", "image/png", "owner", true, "Caption", crop, CancellationToken.None);

            Assert.Equal(1200, photo.Width);
            Assert.Equal(900, photo.Height);
            Assert.Equal("image/webp", photo.ContentType);
            Assert.True(photo.IsCover);

            Assert.True(options.Derivatives.ContainsKey("xs"));

            var derivativePaths = options.Derivatives.Keys
                .Select(key => service.GetDerivativePath(photo, key))
                .ToList();

            Assert.All(derivativePaths, path => Assert.True(File.Exists(path)));
            Assert.All(derivativePaths, path => Assert.EndsWith(".webp", path, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task UpdateCropAsync_ReusesOriginalAndBumpsVersion()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 17);

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var options = CreateOptions();
            var service = CreateService(db, options);

            await using var initial = await CreateTransparentImageStreamAsync(1600, 1200);
            var photo = await service.AddAsync(17, initial, "scene.png", "image/png", "creator", true, null, CancellationToken.None);

            var project = await db.Projects.SingleAsync(p => p.Id == 17);
            Assert.Equal(photo.Version, project.CoverPhotoVersion);

            var derivativeBefore = service.GetDerivativePath(photo, "xl");
            Assert.True(File.Exists(derivativeBefore));
            Assert.EndsWith(".png", derivativeBefore, StringComparison.OrdinalIgnoreCase);

            var updatedCrop = new ProjectPhotoCrop(0, 0, 800, 600);
            var updated = await service.UpdateCropAsync(17, photo.Id, updatedCrop, "creator", CancellationToken.None);

            Assert.NotNull(updated);
            Assert.Equal(2, updated!.Version);
            Assert.Equal(800, updated.Width);
            Assert.Equal(600, updated.Height);
            Assert.Equal("image/png", updated.ContentType);

            project = await db.Projects.SingleAsync(p => p.Id == 17);
            Assert.Equal(updated.Version, project.CoverPhotoVersion);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task AddAsync_SetAsCoverReassignsExistingCover()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 23);

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var options = CreateOptions();
            var service = CreateService(db, options);

            await using var firstStream = await CreateImageStreamAsync(1600, 1200);
            var first = await service.AddAsync(23, firstStream, "first.png", "image/png", "owner", true, null, CancellationToken.None);

            await using var secondStream = await CreateImageStreamAsync(1600, 1200);
            var second = await service.AddAsync(23, secondStream, "second.png", "image/png", "owner", true, null, CancellationToken.None);

            var refreshedFirst = await db.ProjectPhotos.SingleAsync(p => p.Id == first.Id);
            var refreshedProject = await db.Projects.SingleAsync(p => p.Id == 23);

            Assert.False(refreshedFirst.IsCover);
            Assert.True(second.IsCover);
            Assert.Equal(second.Id, refreshedProject.CoverPhotoId);
            Assert.Equal(second.Version, refreshedProject.CoverPhotoVersion);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task AddAsync_StripsMetadataFromDerivatives()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 29);

        await using var stream = await CreateImageStreamWithExifAsync(1600, 1200);

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var options = CreateOptions();
            var service = CreateService(db, options);

            var photo = await service.AddAsync(29, stream, "metadata.jpg", "image/jpeg", "owner", false, null, CancellationToken.None);

            foreach (var key in options.Derivatives.Keys)
            {
                var derivativePath = service.GetDerivativePath(photo, key);
                Assert.True(File.Exists(derivativePath));

                await using var derivativeStream = new FileStream(derivativePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var derivativeImage = await Image.LoadAsync<Rgba32>(derivativeStream);

                Assert.Null(derivativeImage.Metadata.ExifProfile);
                Assert.Null(derivativeImage.Metadata.IptcProfile);
                Assert.Null(derivativeImage.Metadata.XmpProfile);
            }
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task RemoveAsync_DeletesDerivativesAndClearsCover()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 31);

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var options = CreateOptions();
            var service = CreateService(db, options);

            await using var stream = await CreateImageStreamAsync(1600, 1200);
            var photo = await service.AddAsync(31, stream, "delete.png", "image/png", "owner", true, null, CancellationToken.None);

            var paths = options.Derivatives.Keys
                .Select(key => service.GetDerivativePath(photo, key))
                .ToArray();

            var removed = await service.RemoveAsync(31, photo.Id, "owner", CancellationToken.None);
            Assert.True(removed);

            foreach (var path in paths)
            {
                Assert.False(File.Exists(path));
            }

            var project = await db.Projects.SingleAsync(p => p.Id == 31);
            Assert.Null(project.CoverPhotoId);
            Assert.Equal(0, project.CoverPhotoVersion);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task RemoveAsync_PromotesNextPhotoToCover()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 41);

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var options = CreateOptions();
            var service = CreateService(db, options);

            await using var firstStream = await CreateImageStreamAsync(1600, 1200);
            var cover = await service.AddAsync(41, firstStream, "cover.png", "image/png", "owner", true, null, CancellationToken.None);

            await using var secondStream = await CreateImageStreamAsync(1600, 1200);
            var second = await service.AddAsync(41, secondStream, "second.png", "image/png", "owner", false, "Second", CancellationToken.None);

            var removed = await service.RemoveAsync(41, cover.Id, "owner", CancellationToken.None);
            Assert.True(removed);

            var project = await db.Projects.SingleAsync(p => p.Id == 41);
            var refreshedSecond = await db.ProjectPhotos.SingleAsync(p => p.Id == second.Id);

            Assert.True(refreshedSecond.IsCover);
            Assert.Equal(refreshedSecond.Id, project.CoverPhotoId);
            Assert.Equal(refreshedSecond.Version, project.CoverPhotoVersion);
            Assert.Equal(2, refreshedSecond.Version);
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

    private static ProjectPhotoService CreateService(ApplicationDbContext db, ProjectPhotoOptions options)
    {
        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 12, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        return new ProjectPhotoService(db, clock, audit, Options.Create(options), NullLogger<ProjectPhotoService>.Instance);
    }

    private static ProjectPhotoOptions CreateOptions()
    {
        return new ProjectPhotoOptions
        {
            AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "image/png",
                "image/jpeg",
                "image/webp"
            },
            Derivatives = new Dictionary<string, ProjectPhotoDerivativeOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["xl"] = new ProjectPhotoDerivativeOptions { Width = 1600, Height = 1200, Quality = 90 },
                ["md"] = new ProjectPhotoDerivativeOptions { Width = 1200, Height = 900, Quality = 85 },
                ["sm"] = new ProjectPhotoDerivativeOptions { Width = 800, Height = 600, Quality = 80 },
                ["xs"] = new ProjectPhotoDerivativeOptions { Width = 400, Height = 300, Quality = 75 }
            }
        };
    }

    private static async Task<MemoryStream> CreateImageStreamAsync(int width, int height, bool saveAsBmp = false)
    {
        var stream = new MemoryStream();
        using var image = new Image<Rgba32>(width, height, new Rgba32(200, 80, 60, 255));

        if (saveAsBmp)
        {
            await image.SaveAsync(stream, new BmpEncoder());
        }
        else
        {
            await image.SaveAsync(stream, new PngEncoder());
        }

        stream.Position = 0;
        return stream;
    }

    private static async Task<MemoryStream> CreateTransparentImageStreamAsync(int width, int height)
    {
        var stream = new MemoryStream();
        using var image = new Image<Rgba32>(width, height, new Rgba32(50, 120, 200, 120));
        await image.SaveAsync(stream, new PngEncoder());
        stream.Position = 0;
        return stream;
    }

    private static async Task<MemoryStream> CreateImageStreamWithExifAsync(int width, int height)
    {
        var stream = new MemoryStream();
        using var image = new Image<Rgba32>(width, height, new Rgba32(30, 60, 90, 255));

        var exif = new ExifProfile();
        exif.SetValue(ExifTag.ImageDescription, "Test Image");
        image.Metadata.ExifProfile = exif;

        await image.SaveAsync(stream, new JpegEncoder());
        stream.Position = 0;
        return stream;
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "pm-photos-tests", Guid.NewGuid().ToString("N"));
    }

    private static void CleanupTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
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
