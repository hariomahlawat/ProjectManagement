using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Publications;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class BrochurePresetServiceTests
{
    [Fact]
    public async Task HoD_CreateAndLoad_RoundTripsOrderedDurableConfiguration()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, "hod-1", RoleNames.HoD);

        var created = await service.CreateAsync(
            "hod-1",
            "SDD Core Capability",
            "Institutional core project set",
            Configuration(projectOrder: [2, 1]),
            CancellationToken.None);

        var loaded = await service.LoadAsync(created.Preset.Id, CancellationToken.None);

        Assert.Equal("SDD Core Capability", loaded.Preset.Name);
        Assert.Equal(2, loaded.Preset.ProjectCount);
        Assert.Equal(new[] { 2, 1 }, loaded.Configuration.Projects.Select(item => item.ProjectId).ToArray());
        Assert.Equal(202, loaded.Configuration.Projects[0].PrimaryPhotoId);
        Assert.Equal(BrochureImageMode.Single, loaded.Configuration.Projects[0].ImageMode);
        Assert.Equal(101, loaded.Configuration.Projects[1].PrimaryPhotoId);
        Assert.Equal("Digital capability publication", loaded.Configuration.FrontCoverKicker);
        Assert.Equal("Contemporary edition", loaded.Configuration.FrontCoverDescriptor);
        Assert.False(loaded.Configuration.ShowFrontCoverKicker);
        Assert.True(loaded.Configuration.ShowFrontCoverDescriptor);
        Assert.False(loaded.Configuration.ShowFrontCoverSubtitle);
        Assert.Equal("Simulator Development Division", loaded.Configuration.BackCoverKicker);
        Assert.Equal("Prepared for capability engagement", loaded.Configuration.BackCoverStrapline);
        Assert.Equal("2026", loaded.Configuration.BackCoverEdition);
        Assert.True(loaded.Configuration.ShowBackCoverKicker);
        Assert.False(loaded.Configuration.ShowBackCoverStrapline);
        Assert.True(loaded.Configuration.ShowBackCoverEdition);
        Assert.Equal("Procurement & Acquisition:", loaded.Configuration.PrintProcurementHeading);
        Assert.Equal("POINTS OF CONTACT", loaded.Configuration.PrintContactsHeading);
        Assert.Equal("Developing Agency / SDD", loaded.Configuration.PrintDevelopingAgencyHeading);
        Assert.Equal("Manufacturing Agency / 515 ABW", loaded.Configuration.PrintManufacturingAgencyHeading);
        Assert.Equal("Strategic Outlook", loaded.Configuration.PrintVisionaryHeading);
        Assert.Equal("New Simulator Requirements.", loaded.Configuration.PrintNewSimulatorsHeading);
        Assert.Equal(BrochureNarrativeAlignment.Left, loaded.Configuration.NarrativeAlignment);
        Assert.Empty(loaded.Diagnostics);

        // Durable preset contracts intentionally carry no editorial approval authority.
        Assert.Null(typeof(BrochurePresetConfiguration).GetProperty("CoverReviewed"));
        Assert.Null(typeof(BrochurePresetProjectConfiguration).GetProperty("IsReviewed"));
        Assert.Null(typeof(BrochurePresetProjectConfiguration).GetProperty("ReviewFingerprint"));
    }

    [Fact]
    public async Task CreateAndLoad_JustifiedAlignment_RoundTripsAsSchemaFive()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, "hod-1", RoleNames.HoD);
        var configuration = Configuration(projectOrder: [1]) with
        {
            NarrativeAlignment = BrochureNarrativeAlignment.Justified
        };

        var created = await service.CreateAsync(
            "hod-1",
            "Justified Capability",
            null,
            configuration,
            CancellationToken.None);

        var loaded = await service.LoadAsync(created.Preset.Id, CancellationToken.None);
        var stored = await db.BrochurePresets.AsNoTracking().SingleAsync(item => item.Id == created.Preset.Id);

        Assert.Equal(BrochureNarrativeAlignment.Justified, loaded.Configuration.NarrativeAlignment);
        Assert.Equal("Justified", stored.NarrativeAlignment);
        Assert.Equal(5, stored.SettingsSchemaVersion);
    }

    [Fact]
    public async Task Load_LegacySchemaFourPreset_ForcesLeftAlignmentForBackwardCompatibility()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, "hod-1", RoleNames.HoD);
        var configuration = Configuration(projectOrder: [1]) with
        {
            NarrativeAlignment = BrochureNarrativeAlignment.Justified
        };
        var created = await service.CreateAsync(
            "hod-1",
            "Legacy Alignment",
            null,
            configuration,
            CancellationToken.None);

        var stored = await db.BrochurePresets.SingleAsync(item => item.Id == created.Preset.Id);
        stored.SettingsSchemaVersion = 4;
        stored.NarrativeAlignment = "Justified";
        await db.SaveChangesAsync();

        var loaded = await service.LoadAsync(created.Preset.Id, CancellationToken.None);

        Assert.Equal(BrochureNarrativeAlignment.Left, loaded.Configuration.NarrativeAlignment);
    }

    [Fact]
    public async Task Duplicate_RetainsNarrativeAlignment()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, "hod-1", RoleNames.HoD);
        var configuration = Configuration(projectOrder: [1]) with
        {
            NarrativeAlignment = BrochureNarrativeAlignment.Justified
        };
        var created = await service.CreateAsync(
            "hod-1",
            "Alignment Source",
            null,
            configuration,
            CancellationToken.None);

        var duplicate = await service.DuplicateAsync(
            created.Preset.Id,
            "hod-1",
            created.Preset.RowVersion,
            "Alignment Copy",
            null,
            CancellationToken.None);
        var loaded = await service.LoadAsync(duplicate.Preset.Id, CancellationToken.None);

        Assert.Equal(BrochureNarrativeAlignment.Justified, loaded.Configuration.NarrativeAlignment);
    }

    [Fact]
    public async Task NormalAuthorisedUser_CannotMaintainSharedPreset()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, "po-1", RoleNames.ProjectOfficer);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(
            "po-1",
            "Not Allowed",
            null,
            Configuration(projectOrder: [1]),
            CancellationToken.None));
    }

    [Fact]
    public async Task Create_DuplicateProjectSelection_IsRejected()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, "hod-1", RoleNames.HoD);
        var duplicate = Configuration(projectOrder: [1, 1]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            "hod-1",
            "Duplicate Selection",
            null,
            duplicate,
            CancellationToken.None));

        Assert.Contains("same project", exception.Message);
    }

    [Fact]
    public async Task Comdt_CanCreateSharedPreset()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, "comdt-1", RoleNames.Comdt);

        var result = await service.CreateAsync(
            "comdt-1",
            "Command Capability",
            null,
            Configuration(projectOrder: [1]),
            CancellationToken.None);

        Assert.True(result.Preset.Id > 0);
        Assert.Equal("Command Capability", result.Preset.Name);
    }


    [Fact]
    public async Task ITO_CanCreateSharedPresetWithoutCommandRole()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, "ito-1", RoleNames.Ito);

        var result = await service.CreateAsync(
            "ito-1",
            "ITO Capability Publication",
            null,
            Configuration(projectOrder: [1]),
            CancellationToken.None);

        Assert.True(result.Preset.Id > 0);
        Assert.Equal("ITO Capability Publication", result.Preset.Name);
    }

    [Fact]
    public async Task Load_WhenSavedPhotoWasRemoved_FallsBackAndReportsDiagnostic()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, "hod-1", RoleNames.HoD);
        var created = await service.CreateAsync(
            "hod-1",
            "Photo Resilience",
            null,
            Configuration(projectOrder: [1]),
            CancellationToken.None);

        var photo = await db.ProjectPhotos.SingleAsync(item => item.Id == 101);
        db.ProjectPhotos.Remove(photo);
        await db.SaveChangesAsync();

        var loaded = await service.LoadAsync(created.Preset.Id, CancellationToken.None);

        Assert.Null(Assert.Single(loaded.Configuration.Projects).PrimaryPhotoId);
        var diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal("photoUnavailable", diagnostic.Code);
        Assert.Equal(BrochurePresetDiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public async Task Update_WithStaleRowVersion_IsRejected()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, "hod-1", RoleNames.HoD);
        var created = await service.CreateAsync(
            "hod-1",
            "Concurrency Test",
            null,
            Configuration(projectOrder: [1, 2]),
            CancellationToken.None);
        var staleVersion = created.Preset.RowVersion;

        var firstUpdate = await service.UpdateAsync(
            created.Preset.Id,
            "hod-1",
            staleVersion,
            Configuration(projectOrder: [2, 1]),
            CancellationToken.None);

        Assert.NotEqual(staleVersion, firstUpdate.Preset.RowVersion);
        await Assert.ThrowsAsync<BrochurePresetConcurrencyException>(() => service.UpdateAsync(
            created.Preset.Id,
            "hod-1",
            staleVersion,
            Configuration(projectOrder: [1, 2]),
            CancellationToken.None));
    }

    [Fact]
    public async Task Delete_IsSoftAndRemovedFromSharedList()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, "hod-1", RoleNames.HoD);
        var created = await service.CreateAsync(
            "hod-1",
            "Temporary Set",
            null,
            Configuration(projectOrder: [1]),
            CancellationToken.None);

        await service.DeleteAsync(
            created.Preset.Id,
            "hod-1",
            created.Preset.RowVersion,
            CancellationToken.None);

        Assert.Empty(await service.ListAsync(CancellationToken.None));
        var stored = await db.BrochurePresets.IgnoreQueryFilters().SingleAsync(item => item.Id == created.Preset.Id);
        Assert.False(stored.IsActive);
        Assert.Contains("#DELETED#", stored.NormalizedName);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"brochure-presets-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        db.Users.AddRange(
            User("hod-1", "Head of Department"),
            User("comdt-1", "Commandant"),
            User("ito-1", "Information Technology Officer"),
            User("po-1", "Project Officer"));

        db.Projects.AddRange(
            new Project
            {
                Id = 1,
                Name = "Project Alpha",
                LifecycleStatus = ProjectLifecycleStatus.Active,
                CreatedByUserId = "hod-1",
                RowVersion = [1]
            },
            new Project
            {
                Id = 2,
                Name = "Project Bravo",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                CreatedByUserId = "hod-1",
                RowVersion = [1]
            });

        db.ProjectPhotos.AddRange(
            Photo(101, 1, "alpha.jpg"),
            Photo(202, 2, "bravo.jpg"));
        await db.SaveChangesAsync();
    }

    private static ApplicationUser User(string id, string fullName) => new()
    {
        Id = id,
        UserName = $"{id}@test.local",
        NormalizedUserName = $"{id}@test.local".ToUpperInvariant(),
        Email = $"{id}@test.local",
        NormalizedEmail = $"{id}@test.local".ToUpperInvariant(),
        FullName = fullName,
        SecurityStamp = Guid.NewGuid().ToString("N")
    };

    private static ProjectPhoto Photo(int id, int projectId, string fileName) => new()
    {
        Id = id,
        ProjectId = projectId,
        StorageKey = $"test/{fileName}",
        OriginalFileName = fileName,
        ContentType = "image/jpeg",
        Width = 1600,
        Height = 900,
        Version = 1
    };

    private static BrochurePresetConfiguration Configuration(IReadOnlyList<int> projectOrder)
        => new(
            "SDD Capability Brochure",
            "Simulator Development Division",
            "Capability Edition · 2026",
            "Simulators of the Army, by the Army, for the Army",
            BrochureCoverStyle.Institutional,
            BrochureInstitutionalCoverArtwork.ReferenceOriginal,
            BrochureNarrativeSource.ProjectBrief,
            BrochurePublicationProfile.PrintCompact,
            null,
            null,
            "Opening",
            "Future",
            "Procurement",
            "Centre",
            "Developing",
            "Manufacturing",
            "Visionary",
            "New simulators",
            null,
            false,
            true,
            null,
            null,
            .5d,
            .5d,
            projectOrder.Select(projectId => new BrochurePresetProjectConfiguration(
                projectId,
                projectId == 1 ? 101 : 202,
                null,
                .5d,
                .5d,
                .5d,
                .5d,
                BrochureImageMode.Single)).ToArray(),
            FrontCoverKicker: "Digital capability publication",
            FrontCoverDescriptor: "Contemporary edition",
            ShowFrontCoverKicker: false,
            ShowFrontCoverDescriptor: true,
            ShowFrontCoverTitle: true,
            ShowFrontCoverSubtitle: false,
            ShowFrontCoverEdition: true,
            ShowFrontCoverStrapline: true,
            BackCoverKicker: "Simulator Development Division",
            BackCoverStrapline: "Prepared for capability engagement",
            BackCoverEdition: "2026",
            ShowBackCoverKicker: true,
            ShowBackCoverStrapline: false,
            ShowBackCoverEdition: true,
            PrintProcurementHeading: "Procurement & Acquisition:",
            PrintContactsHeading: "POINTS OF CONTACT",
            PrintDevelopingAgencyHeading: "Developing Agency / SDD",
            PrintManufacturingAgencyHeading: "Manufacturing Agency / 515 ABW",
            PrintVisionaryHeading: "Strategic Outlook",
            PrintNewSimulatorsHeading: "New Simulator Requirements.");

    private static BrochurePresetService CreateService(ApplicationDbContext db, string userId, string role)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, userId),
                new Claim(ClaimTypes.Role, role)
            },
            "TestAuth");
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return new BrochurePresetService(
            db,
            accessor,
            new NullAuditService(),
            new FixedClock(new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<BrochurePresetService>.Instance);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class NullAuditService : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
            => Task.CompletedTask;
    }
}
