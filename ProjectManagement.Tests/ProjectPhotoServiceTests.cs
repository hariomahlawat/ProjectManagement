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
                service.AddAsync(1, oversized, "large.png", "image/png", "user-1", false, null, null, CancellationToken.None));
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
                service.AddAsync(5, stream, "sample.bmp", "image/bmp", "auditor", false, null, null, CancellationToken.None));

            Assert.Contains("not allowed", ex.Message, StringComparison.OrdinalIgnoreCase);
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
            var photo = await service.AddAsync(11, stream, "cover.png", "image/png", "owner", true, "Caption", crop, null, CancellationToken.None);

            Assert.Equal(1200, photo.Width);
            Assert.Equal(900, photo.Height);
            Assert.Equal("image/jpeg", photo.ContentType);
            Assert.True(photo.IsCover);
            Assert.False(photo.IsLowResolution);

            Assert.True(options.Derivatives.ContainsKey("xs"));

            var webpPaths = options.Derivatives.Keys
                .Select(key => service.GetDerivativePath(photo, key, preferWebp: true))
                .ToList();
            var fallbackPaths = options.Derivatives.Keys
                .Select(key => service.GetDerivativePath(photo, key, preferWebp: false))
                .ToList();

            Assert.All(webpPaths, path => Assert.True(File.Exists(path)));
            Assert.All(webpPaths, path => Assert.EndsWith(".webp", path, StringComparison.OrdinalIgnoreCase));
            Assert.All(fallbackPaths, path => Assert.True(File.Exists(path)));
            Assert.All(fallbackPaths, path => Assert.EndsWith(".jpg", path, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task AddAsync_AllowsSmallImageAndFlagsLowResolution()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 17);

        await using var stream = await CreateImageStreamAsync(360, 270);

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var options = CreateOptions();
            var service = CreateService(db, options);

            var photo = await service.AddAsync(17, stream, "small.png", "image/png", "auditor", false, null, null, CancellationToken.None);

            Assert.True(photo.IsLowResolution);
            Assert.Equal(360, photo.Width);
            Assert.Equal(270, photo.Height);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task AddAsync_AcceptsCropWithinAspectTolerance()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 13);

        await using var stream = await CreateImageStreamAsync(1600, 1200);

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var options = CreateOptions();
            var service = CreateService(db, options);

            var crop = new ProjectPhotoCrop(0, 0, 867, 650);
            var photo = await service.AddAsync(13, stream, "tolerant.png", "image/png", "user", false, null, crop, null, CancellationToken.None);

            Assert.Equal(867, photo.Width);
            Assert.Equal(650, photo.Height);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task AddAsync_AcceptsCropWithRoundingVariance()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 14);

        await using var stream = await CreateImageStreamAsync(1600, 1200);

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var options = CreateOptions();
            var service = CreateService(db, options);

            var crop = new ProjectPhotoCrop(10, 15, 1066, 800);
            var photo = await service.AddAsync(14, stream, "variance.png", "image/png", "user", false, null, crop, null, CancellationToken.None);

            Assert.Equal(1066, photo.Width);
            Assert.Equal(800, photo.Height);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task AddAsync_RejectsCropOutsideAspectTolerance()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 15);

        await using var stream = await CreateImageStreamAsync(1600, 1200);

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var options = CreateOptions();
            var service = CreateService(db, options);

            var crop = new ProjectPhotoCrop(0, 0, 1065, 800);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddAsync(15, stream, "bad-variance.png", "image/png", "user", false, null, crop, null, CancellationToken.None));
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
            var photo = await service.AddAsync(17, initial, "scene.png", "image/png", "creator", true, null, null, CancellationToken.None);

            var project = await db.Projects.SingleAsync(p => p.Id == 17);
            Assert.Equal(photo.Version, project.CoverPhotoVersion);

            var derivativeBeforeFallback = service.GetDerivativePath(photo, "xl", preferWebp: false);
            var derivativeBeforeWebp = service.GetDerivativePath(photo, "xl", preferWebp: true);
            Assert.True(File.Exists(derivativeBeforeFallback));
            Assert.True(File.Exists(derivativeBeforeWebp));
            Assert.EndsWith(".png", derivativeBeforeFallback, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".webp", derivativeBeforeWebp, StringComparison.OrdinalIgnoreCase);

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
            var first = await service.AddAsync(23, firstStream, "first.png", "image/png", "owner", true, null, null, CancellationToken.None);

            await using var secondStream = await CreateImageStreamAsync(1600, 1200);
            var second = await service.AddAsync(23, secondStream, "second.png", "image/png", "owner", true, null, null, CancellationToken.None);

            var refreshedFirst = await db.ProjectPhotos.SingleAsync(p => p.Id == first.Id);
            var refreshedProject = await db.Projects.SingleAsync(p => p.Id == 23);
            var coverCount = await db.ProjectPhotos.CountAsync(p => p.ProjectId == 23 && p.IsCover);

            Assert.False(refreshedFirst.IsCover);
            Assert.True(second.IsCover);
            Assert.Equal(2, second.Version);
            Assert.Equal(second.Id, refreshedProject.CoverPhotoId);
            Assert.Equal(second.Version, refreshedProject.CoverPhotoVersion);
            Assert.Equal(1, coverCount);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task AddAsync_WithTot_AssignsTot()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 45, ProjectTotStatus.InProgress);
        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 45);

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var options = CreateOptions();
            var service = CreateService(db, options);

            await using var stream = await CreateImageStreamAsync(1600, 1200);
            var photo = await service.AddAsync(45, stream, "tot.png", "image/png", "owner", false, null, tot.Id, CancellationToken.None);

            Assert.Equal(tot.Id, photo.TotId);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task AddAsync_ThrowsWhenTotNotAllowed()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 46, ProjectTotStatus.NotRequired);
        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 46);

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var options = CreateOptions();
            var service = CreateService(db, options);

            await using var stream = await CreateImageStreamAsync(1600, 1200);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddAsync(46, stream, "invalid.png", "image/png", "owner", false, null, tot.Id, CancellationToken.None));
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task OpenDerivativeAsync_UsesConfiguredStorageRoot()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 31);

        ResetUploadRoot();

        var options = CreateOptions();
        var root = CreateTempRoot();
        options.StorageRoot = root;

        try
        {
            await using var stream = await CreateImageStreamAsync(1600, 1200);
            var service = CreateService(db, options);

            var photo = await service.AddAsync(31, stream, "fresh.png", "image/png", "user", true, null, null, CancellationToken.None);

            var derivative = await service.OpenDerivativeAsync(31, photo.Id, "xl", preferWebp: true, CancellationToken.None);

            Assert.NotNull(derivative);
            Assert.True(File.Exists(service.GetDerivativePath(photo, "xl", preferWebp: true)));
            derivative!.Value.Stream.Dispose();
        }
        finally
        {
            CleanupTempRoot(root);
            ResetUploadRoot();
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

            var photo = await service.AddAsync(29, stream, "metadata.jpg", "image/jpeg", "owner", false, null, null, CancellationToken.None);

            foreach (var key in options.Derivatives.Keys)
            {
                var derivativePath = service.GetDerivativePath(photo, key, preferWebp: false);
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
    public async Task UpdateTotAsync_UpdatesAssociation()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 48, ProjectTotStatus.InProgress);
        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 48);

        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var options = CreateOptions();
            var service = CreateService(db, options);

            await using var stream = await CreateImageStreamAsync(1600, 1200);
            var photo = await service.AddAsync(48, stream, "initial.png", "image/png", "owner", false, null, null, CancellationToken.None);

            Assert.Null(photo.TotId);

            var linked = await service.UpdateTotAsync(48, photo.Id, tot.Id, "owner", CancellationToken.None);
            Assert.NotNull(linked);
            Assert.Equal(tot.Id, linked!.TotId);
            Assert.Equal(photo.Version + 1, linked.Version);

            var cleared = await service.UpdateTotAsync(48, photo.Id, null, "owner", CancellationToken.None);
            Assert.NotNull(cleared);
            Assert.Null(cleared!.TotId);
            Assert.Equal(linked.Version + 1, cleared.Version);
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
            var photo = await service.AddAsync(31, stream, "delete.png", "image/png", "owner", true, null, null, CancellationToken.None);

            var paths = options.Derivatives.Keys
                .SelectMany(key => new[]
                {
                    service.GetDerivativePath(photo, key, preferWebp: true),
                    service.GetDerivativePath(photo, key, preferWebp: false)
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
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
            var cover = await service.AddAsync(41, firstStream, "cover.png", "image/png", "owner", true, null, null, CancellationToken.None);

            await using var secondStream = await CreateImageStreamAsync(1600, 1200);
            var second = await service.AddAsync(41, secondStream, "second.png", "image/png", "owner", false, "Second", null, CancellationToken.None);

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

    private static async Task SeedProjectAsync(ApplicationDbContext db, int projectId, ProjectTotStatus? totStatus = null)
    {
        db.Projects.Add(new Project
        {
            Id = projectId,
            Name = $"Project {projectId}",
            CreatedByUserId = "creator",
            RowVersion = new byte[] { 1 }
        });

        if (totStatus.HasValue)
        {
            db.ProjectTots.Add(new ProjectTot
            {
                ProjectId = projectId,
                Status = totStatus.Value
            });
        }
        await db.SaveChangesAsync();
    }

    private static ProjectPhotoService CreateService(ApplicationDbContext db, ProjectPhotoOptions options)
    {
        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 12, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var optionsWrapper = Options.Create(options);
        var documentOptions = Options.Create(new ProjectDocumentOptions());
        var environment = new TestWebHostEnvironment
        {
            ContentRootPath = Path.Combine(Path.GetTempPath(), "pm-photo-tests")
        };
        var uploadRoot = new UploadRootProvider(optionsWrapper, documentOptions, environment, NullLogger<UploadRootProvider>.Instance);
        return new ProjectPhotoService(db, clock, audit, optionsWrapper, uploadRoot, NullLogger<ProjectPhotoService>.Instance);
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
