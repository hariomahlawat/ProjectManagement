using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// The shared ToT drawer service: each hosting page passes its own access rule, and the service
/// applies it to reads, saves and remarks (redesign PR 6a, docs/ux/tot.md).
/// </summary>
public sealed class ProjectTotDrawerServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Details_ReportTheHostPagesAccessRule()
    {
        await using var db = await SeedAsync();
        var service = new ProjectTotDrawerService(db, new FixedClock(Now));

        var allowed = await service.GetDetailsAsync(901, Access(canManage: true), CancellationToken.None);
        var denied = await service.GetDetailsAsync(901, Access(canManage: false), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, allowed.StatusCode);
        Assert.True(Json(allowed).GetProperty("canManage").GetBoolean());
        Assert.False(Json(denied).GetProperty("canManage").GetBoolean());
        Assert.Equal(901, Json(allowed).GetProperty("projectId").GetInt32());
        Assert.Equal("BMP-II Sml", Json(allowed).GetProperty("projectName").GetString());
    }

    [Fact]
    public async Task Save_AndRemark_AreRefusedWhenTheHostSaysNo()
    {
        await using var db = await SeedAsync();
        var service = new ProjectTotDrawerService(db, new FixedClock(Now));
        var input = new ProjectTotDrawerInput { ProjectId = 901, Status = ProjectTotStatus.NotRequired };

        var save = await service.SaveAsync(901, input, Access(canManage: false), CancellationToken.None);
        var remark = await service.AddRemarkAsync(901, 901, "Checked with the user unit.", new[] { "Project Office" }, new NoRemarkService(), Access(canManage: false), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, save.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, remark.StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal(ProjectTotStatus.NotStarted, (await db.ProjectTots.SingleAsync(t => t.ProjectId == 901)).Status);
        Assert.False(await db.Remarks.AnyAsync(r => r.ProjectId == 901));
    }

    [Fact]
    public async Task Save_ByASubmitter_GoesForApproval()
    {
        await using var db = await SeedAsync();
        var service = new ProjectTotDrawerService(db, new FixedClock(Now));
        var input = new ProjectTotDrawerInput { ProjectId = 901, Status = ProjectTotStatus.NotRequired };

        var save = await service.SaveAsync(901, input, Access(canManage: true, canApprove: false), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, save.StatusCode);
        Assert.True(Json(save).GetProperty("submittedForApproval").GetBoolean());
        db.ChangeTracker.Clear();
        Assert.Equal(ProjectTotStatus.NotStarted, (await db.ProjectTots.SingleAsync(t => t.ProjectId == 901)).Status);
        Assert.Equal(ProjectTotStatus.NotRequired, (await db.ProjectTotRequests.SingleAsync(r => r.ProjectId == 901)).ProposedStatus);
    }

    [Fact]
    public async Task Decide_NeedsAnApprover()
    {
        await using var db = await SeedAsync();
        var service = new ProjectTotDrawerService(db, new FixedClock(Now));

        var response = await service.DecideAsync(901, approve: true, "AAAA", null, Access(canManage: true, canApprove: false), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
    }

    private static ProjectTotDrawerAccess Access(bool canManage, bool canApprove = false)
        => new("office-1", (_, _) => Task.FromResult(canManage), canApprove, IsAdmin: false, IsHoD: canApprove);

    private static JsonElement Json(ProjectTotDrawerResponse response) => JsonSerializer.SerializeToElement(response.Body);

    private static async Task<ApplicationDbContext> SeedAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.Users.Add(new ApplicationUser { Id = "office-1", UserName = "office-1", FullName = "Project Office" });
        db.Projects.Add(new Project
        {
            Id = 901,
            Name = "BMP-II Sml",
            CreatedByUserId = "office-1",
            CreatedAt = new DateTime(2008, 1, 1),
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            CompletedYear = 2008,
            RowVersion = new byte[] { 1 },
            Tot = new ProjectTot { ProjectId = 901, Status = ProjectTotStatus.NotStarted }
        });
        await db.SaveChangesAsync();
        return db;
    }

    // Must never be reached: the access check refuses first.
    private sealed class NoRemarkService : ProjectManagement.Services.Remarks.IRemarkService
    {
        public Task<Remark> CreateRemarkAsync(ProjectManagement.Services.Remarks.CreateRemarkRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("not expected");

        public Task<ProjectManagement.Services.Remarks.RemarkListResult> ListRemarksAsync(ProjectManagement.Services.Remarks.ListRemarksRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("not expected");

        public Task<Remark?> EditRemarkAsync(int remarkId, ProjectManagement.Services.Remarks.EditRemarkRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("not expected");

        public Task<bool> SoftDeleteRemarkAsync(int remarkId, ProjectManagement.Services.Remarks.SoftDeleteRemarkRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("not expected");

        public Task<System.Collections.Generic.IReadOnlyList<RemarkAudit>> GetRemarkAuditAsync(int remarkId, ProjectManagement.Services.Remarks.RemarkActorContext actor, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("not expected");
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }
}
