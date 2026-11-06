using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Recovery;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Moving a project to Trash and restoring it are recorded in the project's own history
/// (ProjectAudits), not the global audit log, so the Recovery centre's recent operations never
/// showed them.
/// </summary>
public sealed class AdminRecoverySummaryRecentOperationsTests
{
    [Fact]
    public async Task RecentOperations_IncludeProjectTrashAndRestore_NewestFirst()
    {
        var now = new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        db.Users.Add(new ApplicationUser { Id = "hod", UserName = "hod", FullName = "Head of Dept" });
        db.Projects.Add(new Project { Id = 7, Name = "Radar upgrade", CreatedByUserId = "hod", IsDeleted = true, DeletedAt = now.AddDays(-1) });
        db.ProjectAudits.AddRange(
            new ProjectAudit { ProjectId = 7, Action = "Trash", PerformedByUserId = "hod", PerformedAt = now.AddHours(-3), Reason = "Duplicate" },
            new ProjectAudit { ProjectId = 7, Action = "RestoreTrash", PerformedByUserId = "hod", PerformedAt = now.AddHours(-2) },
            new ProjectAudit { ProjectId = 7, Action = "Trash", PerformedByUserId = "hod", PerformedAt = now.AddHours(-1), Reason = "Duplicate again" });
        db.AuditLogs.Add(new AuditLog { TimeUtc = now.AddMinutes(-30).UtcDateTime, Level = "Warning", Action = "Chat.AttachmentsRemoved", UserName = "admin" });
        await db.SaveChangesAsync();

        var service = new AdminRecoverySummaryService(
            db,
            new AdminTimeService(FakeClock.AtUtc(now)),
            new AuditActionPresentationCatalog(),
            Options.Create(new ProjectRetentionOptions()),
            Options.Create(new AdminRecoveryOptions()));

        var summary = await service.GetAsync();

        Assert.Equal(
            new[] { "Chat.AttachmentsRemoved", "Project.Trash", "Project.RestoreTrash", "Project.Trash" },
            summary.RecentOperations.Select(operation => operation.Action).ToArray());
        var latestTrash = summary.RecentOperations[1];
        Assert.Equal("Project moved to Trash", latestTrash.Title);
        Assert.Equal("Head of Dept", latestTrash.Actor);
        Assert.Equal("Radar upgrade. Reason: Duplicate again", latestTrash.Detail);
    }

    [Fact]
    public async Task RecentOperations_LeaveOutPeoplesOwnNotebookRestores_AndShowNamesNotIds()
    {
        var now = new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        db.Users.Add(new ApplicationUser { Id = "bc62f5aa", UserName = "admin.rao", FullName = "Maj S. Rao" });
        db.AuditLogs.AddRange(
            new AuditLog { TimeUtc = now.AddMinutes(-10).UtcDateTime, Level = "Info", Action = "Notebook.Restore", UserId = "bc62f5aa" },
            new AuditLog { TimeUtc = now.AddMinutes(-20).UtcDateTime, Level = "Info", Action = "Documents.Restore", UserId = "bc62f5aa" });
        await db.SaveChangesAsync();

        var service = new AdminRecoverySummaryService(
            db,
            new AdminTimeService(FakeClock.AtUtc(now)),
            new AuditActionPresentationCatalog(),
            Options.Create(new ProjectRetentionOptions()),
            Options.Create(new AdminRecoveryOptions()));

        var summary = await service.GetAsync();

        var operation = Assert.Single(summary.RecentOperations);
        Assert.Equal("Documents.Restore", operation.Action);
        Assert.Equal("Maj S. Rao", operation.Actor);
    }
}
