using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.Remarks;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcProgressServiceTests
{
    [Fact]
    public async Task GetCurrentProgressAsync_UsesLatestEditedExternalRemarkAndUnlinkedFfcRemark()
    {
        await using var db = CreateDbContext();
        var (record, linkedProject) = await SeedRecordAndProjectAsync(db);
        var linkedFfc = new FfcProject
        {
            FfcRecordId = record.Id,
            Name = "Linked",
            LinkedProjectId = linkedProject.Id,
            Quantity = 1
        };
        var unlinkedFfc = new FfcProject
        {
            FfcRecordId = record.Id,
            Name = "Unlinked",
            Remarks = "  Local FFC update  ",
            Quantity = 1
        };
        db.FfcProjects.AddRange(linkedFfc, unlinkedFfc);
        await db.SaveChangesAsync();

        db.Remarks.AddRange(
            new Remark
            {
                ProjectId = linkedProject.Id,
                AuthorUserId = "user",
                AuthorRole = RemarkActorRole.Administrator,
                Type = RemarkType.External,
                Scope = RemarkScope.General,
                Body = "Older current text",
                EventDate = new DateOnly(2026, 1, 1),
                CreatedAtUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                LastEditedAtUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                RowVersion = new byte[] { 1 }
            },
            new Remark
            {
                ProjectId = linkedProject.Id,
                AuthorUserId = "user",
                AuthorRole = RemarkActorRole.Administrator,
                Type = RemarkType.External,
                Scope = RemarkScope.General,
                Body = "Newer created but not latest edited",
                EventDate = new DateOnly(2026, 1, 10),
                CreatedAtUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                RowVersion = new byte[] { 2 }
            });
        await db.SaveChangesAsync();

        var service = new FfcProgressService(db, new RecordingRemarkService());
        var result = await service.GetCurrentProgressAsync(new[]
        {
            new FfcProgressTarget(linkedFfc.Id, linkedFfc.LinkedProjectId, linkedFfc.Remarks),
            new FfcProgressTarget(unlinkedFfc.Id, null, unlinkedFfc.Remarks)
        });

        var linked = result[linkedFfc.Id];
        Assert.Equal("Older current text", linked.Text);
        Assert.Equal(FfcProgressSource.ExternalProjectRemark, linked.Source);
        Assert.True(linked.IsEditable);
        Assert.NotNull(linked.ExternalRemarkId);

        var unlinked = result[unlinkedFfc.Id];
        Assert.Equal("Local FFC update", unlinked.Text);
        Assert.Equal(FfcProgressSource.FfcProjectRemark, unlinked.Source);
        Assert.False(unlinked.IsEditable);
        Assert.Null(unlinked.ExternalRemarkId);
    }

    [Fact]
    public async Task UpdateProgressAsync_ForLinkedProject_EditsCanonicalExternalRemark()
    {
        await using var db = CreateDbContext();
        var (record, linkedProject) = await SeedRecordAndProjectAsync(db);
        var ffcProject = new FfcProject
        {
            FfcRecordId = record.Id,
            Name = "Linked",
            LinkedProjectId = linkedProject.Id,
            Quantity = 1
        };
        db.FfcProjects.Add(ffcProject);
        await db.SaveChangesAsync();

        var remark = new Remark
        {
            ProjectId = linkedProject.Id,
            AuthorUserId = "user",
            AuthorRole = RemarkActorRole.Administrator,
            Type = RemarkType.External,
            Scope = RemarkScope.General,
            Body = "Before",
            EventDate = new DateOnly(2026, 1, 1),
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RowVersion = new byte[] { 9 }
        };
        db.Remarks.Add(remark);
        await db.SaveChangesAsync();

        var remarkService = new RecordingRemarkService();
        var service = new FfcProgressService(db, remarkService);
        var actor = new RemarkActorContext(
            "admin",
            RemarkActorRole.Administrator,
            new[] { RemarkActorRole.Administrator });

        var result = await service.UpdateProgressAsync(new FfcProgressUpdateCommand(
            FfcProjectId: ffcProject.Id,
            RequestedLinkedProjectId: linkedProject.Id,
            ExternalRemarkId: remark.Id,
            ProgressText: "  Updated from FFC  ",
            Actor: actor));

        Assert.Equal(remark.Id, result.ExternalRemarkId);
        Assert.Equal("Updated from FFC", result.ProgressText);
        Assert.Equal(FfcProgressSource.ExternalProjectRemark, result.Source);
        Assert.Equal(remark.Id, remarkService.EditedRemarkId);
        Assert.NotNull(remarkService.EditRequest);
        Assert.Equal("Updated from FFC", remarkService.EditRequest!.Body);
        Assert.Contains("\"kind\":\"progress\"", remarkService.EditRequest.Meta);
    }

    [Fact]
    public async Task UpdateProgressAsync_ForUnlinkedProject_UpdatesFfcProjectRemarks()
    {
        await using var db = CreateDbContext();
        var (record, _) = await SeedRecordAndProjectAsync(db);
        var ffcProject = new FfcProject
        {
            FfcRecordId = record.Id,
            Name = "Unlinked",
            Quantity = 1
        };
        db.FfcProjects.Add(ffcProject);
        await db.SaveChangesAsync();

        var service = new FfcProgressService(db, new RecordingRemarkService());
        var result = await service.UpdateProgressAsync(new FfcProgressUpdateCommand(
            FfcProjectId: ffcProject.Id,
            RequestedLinkedProjectId: null,
            ExternalRemarkId: null,
            ProgressText: "  Local update  ",
            Actor: null));

        Assert.Equal("Local update", result.ProgressText);
        Assert.Equal(FfcProgressSource.FfcProjectRemark, result.Source);
        Assert.Equal("Local update", await db.FfcProjects
            .Where(project => project.Id == ffcProject.Id)
            .Select(project => project.Remarks)
            .SingleAsync());
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(FfcRecord Record, Project LinkedProject)> SeedRecordAndProjectAsync(
        ApplicationDbContext db)
    {
        var country = new FfcCountry { Name = "Seedland", IsoCode = "SED" };
        db.FfcCountries.Add(country);
        await db.SaveChangesAsync();

        var record = new FfcRecord { CountryId = country.Id, Year = 2026 };
        var project = new Project { Name = "Canonical Project", CreatedByUserId = "user" };
        db.FfcRecords.Add(record);
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return (record, project);
    }

    private sealed class RecordingRemarkService : IRemarkService
    {
        public int? EditedRemarkId { get; private set; }
        public EditRemarkRequest? EditRequest { get; private set; }

        public Task<Remark> CreateRemarkAsync(
            CreateRemarkRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new Remark
            {
                Id = 999,
                ProjectId = request.ProjectId,
                AuthorUserId = request.Actor.UserId,
                AuthorRole = request.Actor.ActorRole,
                Type = request.Type,
                Scope = request.Scope,
                Body = request.Body,
                EventDate = request.EventDate,
                CreatedAtUtc = DateTime.UtcNow,
                RowVersion = new byte[] { 1 }
            });
        }

        public Task<Remark?> EditRemarkAsync(
            int remarkId,
            EditRemarkRequest request,
            CancellationToken cancellationToken = default)
        {
            EditedRemarkId = remarkId;
            EditRequest = request;
            return Task.FromResult<Remark?>(new Remark
            {
                Id = remarkId,
                ProjectId = 1,
                AuthorUserId = request.Actor.UserId,
                AuthorRole = request.Actor.ActorRole,
                Type = RemarkType.External,
                Scope = request.Scope,
                Body = request.Body,
                EventDate = request.EventDate,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
                LastEditedAtUtc = DateTime.UtcNow,
                RowVersion = new byte[] { 2 }
            });
        }

        public Task<RemarkListResult> ListRemarksAsync(
            ListRemarksRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new RemarkListResult(0, Array.Empty<Remark>(), 1, 50));

        public Task<bool> SoftDeleteRemarkAsync(
            int remarkId,
            SoftDeleteRemarkRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<RemarkAudit>> GetRemarkAuditAsync(
            int remarkId,
            RemarkActorContext actor,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RemarkAudit>>(Array.Empty<RemarkAudit>());
    }
}
