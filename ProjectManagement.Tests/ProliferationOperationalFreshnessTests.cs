using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProliferationOperationalFreshnessTests
{
    [Fact]
    public async Task OperationalSnapshot_PrioritisesBusinessChronology_AndSeparatesStaffActivity()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Projects.AddRange(
            new Project
            {
                Id = 1,
                Name = "Project Alpha",
                CaseFileNumber = "PA-01",
                CreatedByUserId = "creator",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                RowVersion = Guid.NewGuid().ToByteArray()
            },
            new Project
            {
                Id = 2,
                Name = "Project Bravo",
                CaseFileNumber = "PB-02",
                CreatedByUserId = "creator",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                RowVersion = Guid.NewGuid().ToByteArray()
            });

        db.Users.Add(new ApplicationUser
        {
            Id = "staff-1",
            UserName = "staff.one",
            FullName = "Staff One"
        });

        var clock = FakeClock.AtUtc(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));

        db.ProliferationGranularEntries.AddRange(
            new ProliferationGranular
            {
                Id = Guid.NewGuid(),
                ProjectId = 1,
                Source = ProliferationSource.Sdd,
                UnitName = "24 SIKH",
                ProliferationDate = new DateOnly(2026, 8, 18),
                Quantity = 2,
                ApprovalStatus = ApprovalStatus.Approved,
                SubmittedByUserId = "staff-1",
                ApprovedByUserId = "staff-1",
                ApprovedOnUtc = new DateTime(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc),
                CreatedOnUtc = new DateTime(2026, 8, 19, 8, 0, 0, DateTimeKind.Utc),
                LastUpdatedOnUtc = new DateTime(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc),
                RowVersion = new byte[] { 1 }
            },
            new ProliferationGranular
            {
                Id = Guid.NewGuid(),
                ProjectId = 2,
                Source = ProliferationSource.Sdd,
                UnitName = "Future Unit",
                ProliferationDate = new DateOnly(2026, 8, 25),
                Quantity = 9,
                ApprovalStatus = ApprovalStatus.Approved,
                SubmittedByUserId = "staff-1",
                ApprovedByUserId = "staff-1",
                ApprovedOnUtc = new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc),
                CreatedOnUtc = new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc),
                LastUpdatedOnUtc = new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc),
                RowVersion = new byte[] { 1 }
            });

        db.ProliferationYearlies.Add(new ProliferationYearly
        {
            Id = Guid.NewGuid(),
            ProjectId = 2,
            Source = ProliferationSource.Abw515,
            Year = 2026,
            TotalQuantity = 11,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "staff-1",
            ApprovedByUserId = "staff-1",
            ApprovedOnUtc = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc),
            CreatedOnUtc = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc),
            LastUpdatedOnUtc = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc),
            RowVersion = new byte[] { 1 }
        });

        db.AuditLogs.AddRange(
            new AuditLog
            {
                TimeUtc = new DateTime(2026, 8, 20, 11, 0, 0, DateTimeKind.Utc),
                Level = "Info",
                Action = "ProjectOfficeReports.Proliferation.GranularRecorded",
                UserId = "staff-1",
                UserName = "staff.one",
                DataJson = JsonSerializer.Serialize(new
                {
                    ProjectId = "1",
                    Source = "SDD",
                    UnitName = "24 SIKH",
                    ProliferationDate = "2026-08-18",
                    Action = "Update"
                })
            },
            new AuditLog
            {
                TimeUtc = new DateTime(2026, 8, 10, 11, 0, 0, DateTimeKind.Utc),
                Level = "Info",
                Action = "ProjectOfficeReports.ProliferationYearlyDecided",
                UserId = "staff-1",
                UserName = "staff.one",
                DataJson = JsonSerializer.Serialize(new
                {
                    ProjectId = "2",
                    Source = "515 ABW",
                    Year = "2026",
                    Approved = "true"
                })
            },
            new AuditLog
            {
                TimeUtc = new DateTime(2026, 8, 20, 11, 30, 0, DateTimeKind.Utc),
                Level = "Info",
                Action = "ProjectOfficeReports.Proliferation.ExportGenerated",
                UserId = "staff-1",
                UserName = "staff.one"
            });

        await db.SaveChangesAsync();

        var service = new ProliferationSummaryReadService(
            new ProliferationAggregateReadService(db),
            db,
            clock);

        var snapshot = await service.GetOperationalSnapshotAsync(
            recentProliferationLimit: 10,
            recentActivityLimit: 5,
            CancellationToken.None);

        Assert.Equal(2, snapshot.RecentProliferation.Count);
        Assert.Equal("Project Alpha", snapshot.RecentProliferation[0].ProjectName);
        Assert.Equal(new DateOnly(2026, 8, 18), snapshot.RecentProliferation[0].ProliferationDate);
        Assert.Equal("Project Bravo", snapshot.RecentProliferation[1].ProjectName);
        Assert.Null(snapshot.RecentProliferation[1].ProliferationDate);
        Assert.DoesNotContain(snapshot.RecentProliferation, x => x.UnitName == "Future Unit");

        Assert.Equal(2, snapshot.StaffActivity.ActionsLast30Days);
        Assert.Equal(1, snapshot.StaffActivity.ActiveStaffLast30Days);
        Assert.Equal(new DateTime(2026, 8, 20, 11, 0, 0, DateTimeKind.Utc), snapshot.StaffActivity.LatestActivityUtc);
        var latest = Assert.Single(snapshot.StaffActivity.RecentActivity, x => x.TimeUtc == snapshot.StaffActivity.LatestActivityUtc);
        Assert.Equal("Updated detailed entry", latest.ActionLabel);
        Assert.Equal("Staff One", latest.ActorDisplayName);
        Assert.Equal("Project Alpha", latest.ProjectName);
    }
}
