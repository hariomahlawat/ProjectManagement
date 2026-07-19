using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Ffc;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcRecordWorkspaceCommandTests
{
    [Fact]
    public async Task RecordCreate_PreservesPendingMilestoneRemarksWithoutPersistingCompletionDate()
    {
        await using var db = CreateDbContext();
        var country = new FfcCountry { Name = "Testland", IsoCode = "TST", IsActive = true };
        db.FfcCountries.Add(country);
        await db.SaveChangesAsync();

        var service = new FfcRecordCommandService(
            db,
            new NoOpAuditService(),
            new HttpContextAccessor(),
            NullLogger<FfcRecordCommandService>.Instance);

        var result = await service.CreateAsync(new FfcRecordCreateCommand(
            CountryId: country.Id,
            Year: 2026,
            IpaCompleted: false,
            IpaDate: null,
            IpaRemarks: "IPA case under examination.",
            GslCompleted: false,
            GslDate: null,
            GslRemarks: "GSL awaited from sponsoring directorate.",
            OverallRemarks: "Current country position.",
            CreatedByUserId: "admin"));

        Assert.True(result.Success);
        var record = await db.FfcRecords.AsNoTracking().SingleAsync();
        Assert.False(record.IpaYes);
        Assert.Null(record.IpaDate);
        Assert.Equal("IPA case under examination.", record.IpaRemarks);
        Assert.False(record.GslYes);
        Assert.Null(record.GslDate);
        Assert.Equal("GSL awaited from sponsoring directorate.", record.GslRemarks);
    }

    [Fact]
    public async Task RecordRestore_ReturnsArchivedRecordToTheActivePortfolio()
    {
        await using var db = CreateDbContext();
        var record = await SeedRecordAsync(db);
        record.IsDeleted = true;
        await db.SaveChangesAsync();

        var service = new FfcRecordCommandService(
            db,
            new NoOpAuditService(),
            new HttpContextAccessor(),
            NullLogger<FfcRecordCommandService>.Instance);

        var result = await service.RestoreAsync(
            record.Id,
            Convert.ToBase64String(record.RowVersion));

        Assert.True(result.Success);
        var restored = await db.FfcRecords.AsNoTracking().SingleAsync(item => item.Id == record.Id);
        Assert.False(restored.IsDeleted);
    }

    [Fact]
    public async Task ProjectSave_LinkedSourceRequiresASelectedPrismProject()
    {
        await using var db = CreateDbContext();
        var record = await SeedRecordAsync(db);
        var service = CreateProjectService(db);

        var result = await service.SaveAsync(new FfcProjectSaveCommand(
            RecordId: record.Id,
            ProjectId: null,
            IsLinkedProject: true,
            DisplayName: "Missing link",
            LinkedProjectId: null,
            Quantity: 1,
            Position: FfcUnitPosition.Planned,
            DeliveredOn: null,
            InstalledOn: null,
            ProgressText: null,
            RowVersion: null,
            Actor: null));

        Assert.False(result.Success);
        Assert.NotNull(result.FieldErrors);
        Assert.Contains("LinkedProjectId", result.FieldErrors!.Keys);
        Assert.Empty(await db.FfcProjects.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ProjectSave_InstalledPositionPersistsDeliveredAndInstalledFlagsTogether()
    {
        await using var db = CreateDbContext();
        var record = await SeedRecordAsync(db);
        var service = CreateProjectService(db);

        var result = await service.SaveAsync(new FfcProjectSaveCommand(
            RecordId: record.Id,
            ProjectId: null,
            IsLinkedProject: false,
            DisplayName: "Installed simulator",
            LinkedProjectId: null,
            Quantity: 2,
            Position: FfcUnitPosition.Installed,
            DeliveredOn: new DateOnly(2026, 5, 1),
            InstalledOn: new DateOnly(2026, 5, 10),
            ProgressText: "Delivered and installed.",
            RowVersion: null,
            Actor: null));

        Assert.True(result.Success);
        var project = await db.FfcProjects.AsNoTracking().SingleAsync();
        Assert.True(project.IsDelivered);
        Assert.True(project.IsInstalled);
        Assert.Equal(new DateOnly(2026, 5, 1), project.DeliveredOn);
        Assert.Equal(new DateOnly(2026, 5, 10), project.InstalledOn);
        Assert.Equal("Delivered and installed.", project.Remarks);
        Assert.NotEmpty(project.RowVersion);
    }

    [Fact]
    public async Task ProjectSave_DoesNotRewriteAnUnchangedCanonicalProgressRemark()
    {
        await using var db = CreateDbContext();
        var record = await SeedRecordAsync(db);
        var linkedProject = new Project
        {
            Name = "Canonical linked project",
            CreatedByUserId = "admin",
            LifecycleStatus = ProjectLifecycleStatus.Active
        };
        db.Projects.Add(linkedProject);
        await db.SaveChangesAsync();

        var ffcProject = new FfcProject
        {
            FfcRecordId = record.Id,
            Name = "Canonical linked project",
            LinkedProjectId = linkedProject.Id,
            Quantity = 1
        };
        db.FfcProjects.Add(ffcProject);
        await db.SaveChangesAsync();

        var progress = new TrackingProgressService(ffcProject.Id, "No change to canonical progress.");
        var service = new FfcProjectCommandService(
            db,
            progress,
            new NoOpAuditService(),
            new HttpContextAccessor(),
            NullLogger<FfcProjectCommandService>.Instance);

        var result = await service.SaveAsync(new FfcProjectSaveCommand(
            RecordId: record.Id,
            ProjectId: ffcProject.Id,
            IsLinkedProject: true,
            DisplayName: ffcProject.Name,
            LinkedProjectId: linkedProject.Id,
            Quantity: 1,
            Position: FfcUnitPosition.Planned,
            DeliveredOn: null,
            InstalledOn: null,
            ProgressText: "No change to canonical progress.",
            RowVersion: Convert.ToBase64String(ffcProject.RowVersion),
            Actor: null));

        Assert.True(result.Success);
        Assert.Equal(1, progress.ReadCount);
        Assert.Equal(0, progress.UpdateCount);
    }

    private static FfcProjectCommandService CreateProjectService(ApplicationDbContext db)
        => new(
            db,
            new NoOpProgressService(),
            new NoOpAuditService(),
            new HttpContextAccessor(),
            NullLogger<FfcProjectCommandService>.Instance);

    private static async Task<FfcRecord> SeedRecordAsync(ApplicationDbContext db)
    {
        var country = new FfcCountry { Name = "Seedland", IsoCode = "SED", IsActive = true };
        db.FfcCountries.Add(country);
        await db.SaveChangesAsync();

        var record = new FfcRecord { CountryId = country.Id, Year = 2026 };
        db.FfcRecords.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class TrackingProgressService(long ffcProjectId, string text) : IFfcProgressService
    {
        public int ReadCount { get; private set; }
        public int UpdateCount { get; private set; }

        public Task<IReadOnlyDictionary<long, FfcProgressSnapshot>> GetCurrentProgressAsync(
            IReadOnlyCollection<FfcProgressTarget> targets,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            IReadOnlyDictionary<long, FfcProgressSnapshot> result =
                new Dictionary<long, FfcProgressSnapshot>
                {
                    [ffcProjectId] = new(
                        FfcProjectId: ffcProjectId,
                        Text: text,
                        ExternalRemarkId: 10,
                        Source: FfcProgressSource.ExternalProjectRemark,
                        IsEditable: true)
                };
            return Task.FromResult(result);
        }

        public Task<FfcProgressUpdateResult> UpdateProgressAsync(
            FfcProgressUpdateCommand command,
            CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            throw new InvalidOperationException("An unchanged progress remark must not be rewritten.");
        }
    }

    private sealed class NoOpProgressService : IFfcProgressService
    {
        public Task<IReadOnlyDictionary<long, FfcProgressSnapshot>> GetCurrentProgressAsync(
            IReadOnlyCollection<FfcProgressTarget> targets,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<long, FfcProgressSnapshot>>(
                new Dictionary<long, FfcProgressSnapshot>());

        public Task<FfcProgressUpdateResult> UpdateProgressAsync(
            FfcProgressUpdateCommand command,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Progress updates are not expected in this test.");
    }

    private sealed class NoOpAuditService : IAuditService
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
