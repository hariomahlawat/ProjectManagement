using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Projects;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectProliferationProfileServiceTests
{
    [Fact]
    public async Task UpdateAsync_WithCompleteProfile_PersistsCostAvailabilityAndRemarks()
    {
        await using var db = CreateContext();
        db.Projects.Add(Project(1, "AURA"));
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var service = new ProjectProliferationProfileService(db, clock, audit);

        var result = await service.UpdateAsync(
            new ProjectProliferationUpdateCommand(
                1,
                24.50m,
                true,
                null,
                "Suitable for limited proliferation after user validation."),
            "user-1",
            "Hari Om Ahlawat");

        Assert.True(result.IsSuccess);
        Assert.Equal(24.50m, result.Profile!.CostLakhs);
        Assert.True(result.Profile.AvailableForProliferation == true);
        Assert.Equal("Suitable for limited proliferation after user validation.", result.Profile.Remarks);

        var cost = await db.ProjectProductionCostFacts.SingleAsync(item => item.ProjectId == 1);
        Assert.Equal(24.50m, cost.ApproxProductionCost);
        Assert.Equal("user-1", cost.UpdatedByUserId);

        var status = await db.ProjectTechStatuses.SingleAsync(item => item.ProjectId == 1);
        Assert.True(status.AvailableForProliferation == true);
        Assert.Null(status.NotAvailableReason);
        Assert.Equal("Suitable for limited proliferation after user validation.", status.ProliferationRemarks);

        var log = Assert.Single(audit.Entries);
        Assert.Equal("Project.ProliferationProfileUpdated", log.Action);
        Assert.Equal("24.5", log.Data["CostAfterLakhs"]);
        Assert.Equal("Available", log.Data["AvailabilityAfter"]);
    }

    [Fact]
    public async Task UpdateAsync_NotAvailableWithoutReason_ReturnsValidationError()
    {
        await using var db = CreateContext();
        db.Projects.Add(Project(2, "ASTRAE"));
        await db.SaveChangesAsync();

        var service = new ProjectProliferationProfileService(
            db,
            new FixedClock(DateTimeOffset.UtcNow),
            new RecordingAudit());

        var result = await service.UpdateAsync(
            new ProjectProliferationUpdateCommand(2, null, false, " ", null),
            "user-2",
            "User Two");

        Assert.Equal(ProjectProliferationUpdateStatus.ValidationFailed, result.Status);
        Assert.Contains(nameof(ProjectProliferationUpdateCommand.NotAvailableReason), result.Errors.Keys);
        Assert.Empty(await db.ProjectTechStatuses.ToListAsync());
    }

    [Fact]
    public async Task UpdateAsync_NotAssessed_ClearsPreviousAvailabilityReasonButRetainsTechnologyStatus()
    {
        await using var db = CreateContext();
        db.Projects.Add(Project(3, "NIRBHAYA"));
        db.ProjectTechStatuses.Add(new ProjectTechStatus
        {
            ProjectId = 3,
            TechStatus = ProjectTechStatusCodes.Outdated,
            AvailableForProliferation = false,
            NotAvailableReason = "Technology refresh required.",
            ProliferationRemarks = "Review after upgrade.",
            MarkedAtUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            MarkedByUserId = "older-user"
        });
        await db.SaveChangesAsync();

        var service = new ProjectProliferationProfileService(
            db,
            new FixedClock(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero)),
            new RecordingAudit());

        var result = await service.UpdateAsync(
            new ProjectProliferationUpdateCommand(3, null, null, "Ignored", "Assessment pending."),
            "user-3",
            "User Three");

        Assert.True(result.IsSuccess);
        var status = await db.ProjectTechStatuses.SingleAsync(item => item.ProjectId == 3);
        Assert.Equal(ProjectTechStatusCodes.Outdated, status.TechStatus);
        Assert.Null(status.AvailableForProliferation);
        Assert.Null(status.NotAvailableReason);
        Assert.Equal("Assessment pending.", status.ProliferationRemarks);
    }

    [Fact]
    public async Task UpdateAsync_ZeroCost_PersistsExplicitZeroAndKeepsItDistinctFromMissing()
    {
        await using var db = CreateContext();
        db.Projects.Add(Project(4, "MU-UGV"));
        await db.SaveChangesAsync();

        var audit = new RecordingAudit();
        var service = new ProjectProliferationProfileService(
            db,
            new FixedClock(new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero)),
            audit);

        var result = await service.UpdateAsync(
            new ProjectProliferationUpdateCommand(4, 0m, null, null, null),
            "user-4",
            "User Four");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Profile);
        Assert.Equal(0m, result.Profile!.CostLakhs);
        Assert.Equal("₹0 lakh", result.Profile.CostDisplay);
        Assert.NotEqual("Cost not recorded", result.Profile.CostDisplay);

        var cost = await db.ProjectProductionCostFacts.SingleAsync(item => item.ProjectId == 4);
        Assert.Equal(0m, cost.ApproxProductionCost);
        Assert.Equal("0", Assert.Single(audit.Entries).Data["CostAfterLakhs"]);
    }

    [Fact]
    public async Task UpdateAsync_NegativeCost_ReturnsValidationError()
    {
        await using var db = CreateContext();
        db.Projects.Add(Project(5, "NEGATIVE-COST"));
        await db.SaveChangesAsync();

        var service = new ProjectProliferationProfileService(
            db,
            new FixedClock(DateTimeOffset.UtcNow),
            new RecordingAudit());

        var result = await service.UpdateAsync(
            new ProjectProliferationUpdateCommand(5, -0.01m, null, null, null),
            "user-5",
            "User Five");

        Assert.Equal(ProjectProliferationUpdateStatus.ValidationFailed, result.Status);
        Assert.Contains(nameof(ProjectProliferationUpdateCommand.CostLakhs), result.Errors.Keys);
        Assert.Empty(await db.ProjectProductionCostFacts.ToListAsync());
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Project Project(int id, string name) => new()
    {
        Id = id,
        Name = name,
        CreatedAt = new DateTime(2026, 1, 1),
        CreatedByUserId = "creator",
        LifecycleStatus = ProjectLifecycleStatus.Active
    };

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class RecordingAudit : IAuditService
    {
        public List<AuditEntry> Entries { get; } = new();

        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
        {
            Entries.Add(new AuditEntry(action, data ?? new Dictionary<string, string?>()));
            return Task.CompletedTask;
        }
    }

    private sealed record AuditEntry(string Action, IDictionary<string, string?> Data);
}
