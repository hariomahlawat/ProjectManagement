using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Utilities.PartialDates;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// The project page's ToT drawer: a pending request shown as Now → Proposed, and the HoD's
/// decision taken in place under the register's rules (redesign PR 5, docs/ux/tot.md).
/// </summary>
public sealed class ProjectOverviewTotDecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Details_ShowOnlyTheChangedFields_AndOfferTheDecisionToTheHod()
    {
        await using var db = ProjectOverviewLifecycleTests.CreateContextForTot();
        await SeedPendingAsync(db, 801);

        var hod = await GetDetailsAsync(db, 801, "hod-t", "HoD");
        var pending = hod.GetProperty("pending");
        Assert.True(pending.GetProperty("canDecide").GetBoolean());
        Assert.Equal("K Srinath", pending.GetProperty("submittedBy").GetString());
        Assert.Equal("03 Nov 2026, 10:40", pending.GetProperty("submittedOn").GetString());
        var changes = pending.GetProperty("changes").EnumerateArray()
            .Select(change => $"{change.GetProperty("field").GetString()}: {change.GetProperty("now").GetString()} -> {change.GetProperty("proposed").GetString()}")
            .ToArray();
        Assert.Equal(new[] { "Status: In progress -> Completed", "Completed: — -> Oct 2026" }, changes);
        Assert.False(string.IsNullOrEmpty(pending.GetProperty("rowVersion").GetString()));

        var po = await GetDetailsAsync(db, 801, "po-t", "Project Officer");
        Assert.False(po.GetProperty("pending").GetProperty("canDecide").GetBoolean());
    }

    [Fact]
    public async Task Decide_ApprovesWithTheCurrentVersion()
    {
        await using var db = ProjectOverviewLifecycleTests.CreateContextForTot();
        var version = await SeedPendingAsync(db, 802);

        var (status, body) = await DecideAsync(db, 802, "hod-t", "HoD", approve: true, version, reason: null);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal("Transfer of Technology update approved.", body.GetProperty("message").GetString());
        db.ChangeTracker.Clear();
        var tot = await db.ProjectTots.SingleAsync(item => item.ProjectId == 802);
        Assert.Equal(ProjectTotStatus.Completed, tot.Status);
        Assert.Equal(new DateOnly(2026, 10, 31), tot.CompletedOn);
        Assert.Equal(ProjectTotRequestDecisionState.Approved, (await db.ProjectTotRequests.SingleAsync(r => r.ProjectId == 802)).DecisionState);
    }

    [Fact]
    public async Task Decide_RejectsWithAReason_AndShowsItInHistory()
    {
        await using var db = ProjectOverviewLifecycleTests.CreateContextForTot();
        var version = await SeedPendingAsync(db, 803);

        var (status, _) = await DecideAsync(db, 803, "hod-t", "HoD", approve: false, version, "  Completion certificate not attached.  ");

        Assert.Equal(StatusCodes.Status200OK, status);
        db.ChangeTracker.Clear();
        Assert.Equal(ProjectTotStatus.InProgress, (await db.ProjectTots.SingleAsync(item => item.ProjectId == 803)).Status);

        var details = await GetDetailsAsync(db, 803, "hod-t", "HoD");
        Assert.Equal(JsonValueKind.Null, details.GetProperty("pending").ValueKind);
        var rejected = details.GetProperty("history").EnumerateArray().First();
        Assert.Equal("Completion certificate not attached.", rejected.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("po-t", "Project Officer", StatusCodes.Status403Forbidden)]
    [InlineData("po-other", "Project Office", StatusCodes.Status403Forbidden)]
    public async Task Decide_IsRefusedToNonApprovers(string userId, string role, int expected)
    {
        await using var db = ProjectOverviewLifecycleTests.CreateContextForTot();
        var version = await SeedPendingAsync(db, 804);

        var (status, _) = await DecideAsync(db, 804, userId, role, approve: true, version, reason: null);

        Assert.Equal(expected, status);
        await AssertStillPendingAsync(db, 804);
    }

    [Fact]
    public async Task Decide_RefusesARejectionWithoutAReason()
    {
        await using var db = ProjectOverviewLifecycleTests.CreateContextForTot();
        var version = await SeedPendingAsync(db, 805);

        var (status, body) = await DecideAsync(db, 805, "hod-t", "HoD", approve: false, version, reason: "   ");

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
        await AssertStillPendingAsync(db, 805);
    }

    [Theory]
    [InlineData(null, StatusCodes.Status400BadRequest)]
    [InlineData("not base64!", StatusCodes.Status400BadRequest)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==", StatusCodes.Status409Conflict)]
    public async Task Decide_NeedsTheRequestsCurrentVersion(string? version, int expected)
    {
        await using var db = ProjectOverviewLifecycleTests.CreateContextForTot();
        await SeedPendingAsync(db, 806);

        var (status, _) = await DecideAsync(db, 806, "hod-t", "HoD", approve: true, version, reason: null);

        Assert.Equal(expected, status);
        await AssertStillPendingAsync(db, 806);
    }

    [Fact]
    public async Task Decide_RefusesAnOverlongReason()
    {
        await using var db = ProjectOverviewLifecycleTests.CreateContextForTot();
        var version = await SeedPendingAsync(db, 807);

        var (status, _) = await DecideAsync(db, 807, "hod-t", "HoD", approve: false, version, new string('x', 513));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        await AssertStillPendingAsync(db, 807);
    }

    private static async Task<JsonElement> GetDetailsAsync(ApplicationDbContext db, int projectId, string userId, string role)
    {
        var page = ProjectOverviewLifecycleTests.CreateTotOverviewPage(db, Now, userId, role);
        var result = Assert.IsType<JsonResult>(await page.OnGetTotDetailsAsync(projectId, CancellationToken.None));
        return JsonSerializer.SerializeToElement(result.Value);
    }

    private static async Task<(int Status, JsonElement Body)> DecideAsync(
        ApplicationDbContext db, int projectId, string userId, string role, bool approve, string? version, string? reason)
    {
        var page = ProjectOverviewLifecycleTests.CreateTotOverviewPage(db, Now, userId, role);
        var result = Assert.IsType<JsonResult>(await page.OnPostTotDecideAsync(projectId, approve, version, reason, CancellationToken.None));
        return (result.StatusCode ?? StatusCodes.Status200OK, JsonSerializer.SerializeToElement(result.Value));
    }

    private static async Task AssertStillPendingAsync(ApplicationDbContext db, int projectId)
    {
        db.ChangeTracker.Clear();
        Assert.Equal(ProjectTotRequestDecisionState.Pending, (await db.ProjectTotRequests.SingleAsync(r => r.ProjectId == projectId)).DecisionState);
        Assert.Equal(ProjectTotStatus.InProgress, (await db.ProjectTots.SingleAsync(t => t.ProjectId == projectId)).Status);
    }

    // An in-progress ToT with a PO's request to mark it completed in Oct 2026; returns its version token.
    private static async Task<string> SeedPendingAsync(ApplicationDbContext db, int projectId)
    {
        if (!await db.Users.AnyAsync(user => user.Id == "po-t"))
        {
            db.Users.AddRange(
                new ApplicationUser { Id = "po-t", UserName = "po-t", FullName = "K Srinath" },
                new ApplicationUser { Id = "hod-t", UserName = "hod-t", FullName = "Hari Om Ahlawat" });
        }

        db.Projects.Add(new Project
        {
            Id = projectId,
            Name = "Demo · Weapon Training Simulator (Rifle)",
            CreatedByUserId = "po-t",
            CreatedAt = new DateTime(2024, 1, 1),
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            CompletedYear = 2026,
            LeadPoUserId = "po-t",
            RowVersion = new byte[] { 1 },
            Tot = new ProjectTot
            {
                ProjectId = projectId,
                Status = ProjectTotStatus.InProgress,
                StartedOn = new DateOnly(2026, 9, 1),
                StartDatePrecision = PartialDatePrecision.Day
            }
        });
        db.ProjectTotRequests.Add(new ProjectTotRequest
        {
            ProjectId = projectId,
            ProposedStatus = ProjectTotStatus.Completed,
            ProposedStartedOn = new DateOnly(2026, 9, 1),
            ProposedStartDatePrecision = PartialDatePrecision.Day,
            ProposedCompletedOn = new DateOnly(2026, 10, 31),
            ProposedCompletionDatePrecision = PartialDatePrecision.Month,
            SubmittedByUserId = "po-t",
            SubmittedOnUtc = new DateTime(2026, 11, 3, 5, 10, 0, DateTimeKind.Utc),
            DecisionState = ProjectTotRequestDecisionState.Pending
        });
        await db.SaveChangesAsync();

        var request = await db.ProjectTotRequests.AsNoTracking().SingleAsync(r => r.ProjectId == projectId);
        return Convert.ToBase64String(request.RowVersion);
    }
}
