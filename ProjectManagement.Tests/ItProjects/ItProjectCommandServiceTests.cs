using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services.ItProjects;
using Xunit;
using static ProjectManagement.Tests.ItProjects.ItProjectStageRulesTests;

namespace ProjectManagement.Tests.ItProjects;

public class ItProjectCommandServiceTests
{
    [Fact]
    public async Task Create_WritesTheProjectItsNineStagesAndAnAuditRow()
    {
        await using var db = await ItProjectTestDb.CreateAsync();

        var (result, id) = await db.Commands().CreateAsync(ItProjectTestDb.Details("  LAN upgrade  ", " IT PPP 14/2026 ", 2026), ItProjectTestDb.Actor("ito-1"));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        await using var context = db.NewContext();
        var project = await context.ItProjects.Include(p => p.Stages).SingleAsync(p => p.Id == id);
        Assert.Equal("LAN upgrade", project.Title);
        Assert.Equal("IT PPP 14/2026", project.PppReferenceNumber);
        Assert.Equal(2026, project.FinancialYearStart);
        Assert.Equal("ito-1", project.CreatedByUserId);
        Assert.Equal(ItProjectTestDb.Now.UtcDateTime, project.CreatedAtUtc);
        Assert.NotEmpty(project.RowVersion);
        Assert.Equal(ItProjectStages.Codes, project.Stages.OrderBy(s => s.SortOrder).Select(s => s.StageCode));
        Assert.Equal(new[] { "BM", "PNC" }, project.Stages.Where(s => s.IsSkipped).OrderBy(s => s.SortOrder).Select(s => s.StageCode));
        Assert.Equal(ItProjectStatusKind.NotStarted, ItProjectStatus.Resolve(project).Kind);

        var audit = Assert.Single(db.Audit.Entries);
        Assert.Equal("ItProjects.Created", audit.Action);
        Assert.Equal("ito-1", audit.UserId);
        Assert.Equal(id.ToString(), audit.Data["ItProjectId"]);
        Assert.Equal("2026-27", audit.Data["FinancialYear"]);
    }

    [Fact]
    public async Task Create_RejectsMissingRequiredFields()
    {
        await using var db = await ItProjectTestDb.CreateAsync();

        var (result, id) = await db.Commands().CreateAsync(new ItProjectDetailsInput(" ", "", 1900, ""), ItProjectTestDb.Actor());

        Assert.False(result.Succeeded);
        Assert.Equal(0, id);
        Assert.Contains("Give the project a title.", result.Errors);
        Assert.Contains("Give the IT ARPPP reference number.", result.Errors);
        Assert.Contains("Give the project a description.", result.Errors);
        Assert.Contains("Choose the financial year.", result.Errors);
        Assert.Empty(db.Audit.Entries);
    }

    [Fact]
    public async Task Create_AllowsTheSameReferenceNumberTwice()
    {
        await using var db = await ItProjectTestDb.CreateAsync();

        await db.SeedAsync("First", "IT PPP 1/2026");
        var (result, _) = await db.Commands().CreateAsync(ItProjectTestDb.Details("Second", "IT PPP 1/2026"), ItProjectTestDb.Actor());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SaveStages_AppliesTheRulesReportsTheDiffAndAudits()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();

        var save = await db.Commands().SaveStagesAsync(
            id,
            Input(S("TEC", true, new DateOnly(2025, 11, 10)), S("SO", cost: 46.8m)),
            rowVersion,
            ItProjectTestDb.Actor("hod-1"));

        Assert.True(save.Result.Succeeded, string.Join("; ", save.Result.Errors));
        Assert.Equal(new[] { "PPP", "AON", "BID", "TEC" }, save.AutoCompletedStageCodes);
        Assert.Empty(save.ReopenedStageCodes);

        await using var context = db.NewContext();
        var project = await context.ItProjects.Include(p => p.Stages).SingleAsync(p => p.Id == id);
        var status = ItProjectStatus.Resolve(project);
        Assert.Equal(ItProjectStatusKind.InProgress, status.Kind);
        Assert.Equal("COB", status.CurrentStageCode);
        Assert.Equal(new DateOnly(2025, 11, 10), project.Stages.Single(s => s.StageCode == "TEC").CompletedOn);
        Assert.Null(project.Stages.Single(s => s.StageCode == "BID").CompletedOn);
        Assert.Equal(46.8m, project.Stages.Single(s => s.StageCode == "SO").CostLakhs);
        Assert.Equal("hod-1", project.UpdatedByUserId);
        Assert.NotEqual(rowVersion, project.RowVersion);

        var audit = db.Audit.Entries.Last();
        Assert.Equal("ItProjects.StagesChanged", audit.Action);
        Assert.Equal("Not started", audit.Data["StatusBefore"]);
        Assert.Equal("In progress · COB", audit.Data["StatusAfter"]);
        Assert.Equal("PPP,AON,BID,TEC", audit.Data["AutoCompleted"]);
        Assert.Equal("done 2025-11-10", audit.Data["Stage.TEC"]);
        Assert.Equal("open; cost 46.80 L", audit.Data["Stage.SO"]);
    }

    [Fact]
    public async Task SaveStages_RejectsInvalidDatesWithoutWriting()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();

        var save = await db.Commands().SaveStagesAsync(
            id,
            Input(S("PPP", true, new DateOnly(2026, 6, 1)), S("AON", true, new DateOnly(2026, 5, 1))),
            rowVersion,
            ItProjectTestDb.Actor());

        Assert.False(save.Result.Succeeded);
        Assert.Contains(save.Result.Errors, error => error.Contains("AoN cannot be completed before IT ARPPP / IPA"));
        await using var context = db.NewContext();
        Assert.False(await context.ItProjectStages.AnyAsync(s => s.ItProjectId == id && s.IsCompleted));
        Assert.DoesNotContain(db.Audit.Entries, entry => entry.Action == "ItProjects.StagesChanged");
    }

    [Fact]
    public async Task SaveStages_WithAStaleRowVersionIsAConcurrencyConflict()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, stale) = await db.SeedAsync();

        var first = await db.Commands().SaveStagesAsync(id, Input(S("AON", true)), stale, ItProjectTestDb.Actor("a"));
        Assert.True(first.Result.Succeeded);

        var second = await db.Commands().SaveStagesAsync(id, Input(S("BID", true)), stale, ItProjectTestDb.Actor("b"));

        Assert.False(second.Result.Succeeded);
        Assert.True(second.Result.ConcurrencyConflict);
        Assert.Contains(ItProjectCommandService.ConcurrencyConflictMessage, second.Result.Errors);
        await using var context = db.NewContext();
        Assert.False((await context.ItProjectStages.SingleAsync(s => s.ItProjectId == id && s.StageCode == "BID")).IsCompleted);
    }

    [Fact]
    public async Task UpdateDetails_ChangesTheFieldsAndAuditsBeforeAndAfter()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();

        var result = await db.Commands().UpdateDetailsAsync(id, new ItProjectDetailsInput("Core switches (HA)", "IT PPP 09/2025-A", 2026, "Updated."), rowVersion, ItProjectTestDb.Actor("hod-1"));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        await using var context = db.NewContext();
        var project = await context.ItProjects.SingleAsync(p => p.Id == id);
        Assert.Equal("Core switches (HA)", project.Title);
        Assert.Equal(2026, project.FinancialYearStart);
        var audit = db.Audit.Entries.Last();
        Assert.Equal("ItProjects.Updated", audit.Action);
        Assert.Equal("Core switches, Admin block", audit.Data["BeforeTitle"]);
        Assert.Equal("2025-26", audit.Data["BeforeFinancialYear"]);
        Assert.Equal("2026-27", audit.Data["FinancialYear"]);
    }

    [Fact]
    public async Task Drop_NeedsAReasonMarksTheProjectAndKeepsItsStages()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();
        var staged = await db.Commands().SaveStagesAsync(id, Input(S("AON", true)), rowVersion, ItProjectTestDb.Actor());
        Assert.True(staged.Result.Succeeded);
        rowVersion = await db.RowVersionAsync(id);

        var noReason = await db.Commands().DropAsync(id, new DateOnly(2026, 11, 8), "  ", rowVersion, ItProjectTestDb.Actor());
        Assert.False(noReason.Succeeded);

        var dropped = await db.Commands().DropAsync(id, new DateOnly(2026, 11, 8), "Merged into HQ contract", rowVersion, ItProjectTestDb.Actor("hod-1"));
        Assert.True(dropped.Succeeded, string.Join("; ", dropped.Errors));

        await using var context = db.NewContext();
        var project = await context.ItProjects.Include(p => p.Stages).SingleAsync(p => p.Id == id);
        Assert.Equal(new DateOnly(2026, 11, 8), project.DroppedOn);
        Assert.Equal("hod-1", project.DroppedByUserId);
        var status = ItProjectStatus.Resolve(project);
        Assert.Equal(ItProjectStatusKind.Dropped, status.Kind);
        Assert.Equal("BID", status.CurrentStageCode);
        Assert.Equal("ItProjects.Dropped", db.Audit.Entries.Last().Action);

        var again = await db.Commands().DropAsync(id, new DateOnly(2026, 11, 8), "Again", await db.RowVersionAsync(id), ItProjectTestDb.Actor());
        Assert.False(again.Succeeded);
        Assert.Contains("This IT project is already dropped.", again.Errors);

        var reinstated = await db.Commands().ReinstateAsync(id, await db.RowVersionAsync(id), ItProjectTestDb.Actor());
        Assert.True(reinstated.Succeeded);
        await using var after = db.NewContext();
        var reinstatedProject = await after.ItProjects.Include(p => p.Stages).SingleAsync(p => p.Id == id);
        Assert.Null(reinstatedProject.DroppedOn);
        Assert.Equal(ItProjectStatusKind.InProgress, ItProjectStatus.Resolve(reinstatedProject).Kind);
        Assert.Equal("ItProjects.Reinstated", db.Audit.Entries.Last().Action);
    }

    [Fact]
    public async Task Drop_IsRefusedOnACompletedProject()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();
        var done = await db.Commands().SaveStagesAsync(id, Input(S("DLV", true)), rowVersion, ItProjectTestDb.Actor());
        Assert.True(done.Result.Succeeded);

        var dropped = await db.Commands().DropAsync(id, new DateOnly(2026, 11, 8), "Too late", await db.RowVersionAsync(id), ItProjectTestDb.Actor());

        Assert.False(dropped.Succeeded);
        Assert.Contains(dropped.Errors, error => error.Contains("completed"));
    }

    [Fact]
    public async Task Delete_IsSoftAndHidesTheProjectFromReads()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();

        var result = await db.Commands().DeleteAsync(id, rowVersion, ItProjectTestDb.Actor("admin-1"));

        Assert.True(result.Succeeded);
        await using var context = db.NewContext();
        var row = await context.ItProjects.SingleAsync(p => p.Id == id);
        Assert.True(row.IsDeleted);
        Assert.Equal("admin-1", row.DeletedByUserId);
        Assert.Equal(9, await context.ItProjectStages.CountAsync(s => s.ItProjectId == id));
        Assert.Null(await db.Reads().GetAsync(id));
        Assert.Empty((await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All))).Rows);

        var again = await db.Commands().DeleteAsync(id, await db.RowVersionAsync(id), ItProjectTestDb.Actor());
        Assert.False(again.Succeeded);
        Assert.Contains(ItProjectCommandService.NotFoundMessage, again.Errors);
    }

    [Fact]
    public async Task Drop_RefusesADateLaterThanTodayInIndia()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();

        // The test clock is 8 Nov 2026, 15:00 IST.
        var tomorrow = await db.Commands().DropAsync(id, new DateOnly(2026, 11, 9), "Merged", rowVersion, ItProjectTestDb.Actor());
        Assert.False(tomorrow.Succeeded);
        Assert.Contains(ItProjectCommandService.DroppedInFutureMessage, tomorrow.Errors);
        Assert.Empty(db.Audit.Entries.Where(entry => entry.Action == "ItProjects.Dropped"));

        var today = await db.Commands().DropAsync(id, new DateOnly(2026, 11, 8), "Merged", await db.RowVersionAsync(id), ItProjectTestDb.Actor());
        Assert.True(today.Succeeded, string.Join("; ", today.Errors));
    }

    [Fact]
    public async Task SaveStages_IsRefusedWhileDropped_SoADroppedCaseCannotBeCompleted()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();
        Assert.True((await db.Commands().DropAsync(id, new DateOnly(2026, 11, 1), "Merged", rowVersion, ItProjectTestDb.Actor())).Succeeded);
        var auditCount = db.Audit.Entries.Count;

        var save = await db.Commands().SaveStagesAsync(id, Input(S("DLV", true)), await db.RowVersionAsync(id), ItProjectTestDb.Actor());

        Assert.False(save.Result.Succeeded);
        Assert.Contains(ItProjectCommandService.DroppedStagesMessage, save.Result.Errors);
        Assert.Equal(auditCount, db.Audit.Entries.Count);
        await using var context = db.NewContext();
        var project = await context.ItProjects.Include(p => p.Stages).SingleAsync(p => p.Id == id);
        Assert.DoesNotContain(project.Stages, stage => stage.IsCompleted);

        // Once reinstated, the stages can move again.
        Assert.True((await db.Commands().ReinstateAsync(id, await db.RowVersionAsync(id), ItProjectTestDb.Actor())).Succeeded);
        var after = await db.Commands().SaveStagesAsync(id, Input(S("AON", true)), await db.RowVersionAsync(id), ItProjectTestDb.Actor());
        Assert.True(after.Result.Succeeded, string.Join("; ", after.Result.Errors));
    }

    [Fact]
    public async Task UpdateDetails_MovingTheYearLater_RemovesTheYearRowsItOvertakes()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync(fy: 2024);
        Assert.True((await db.Commands().SaveYearAsync(id, 2025, new ItProjectYearInput(ItProjectYearType.CommittedLiability, "IT PPP 02/2025"), rowVersion, ItProjectTestDb.Actor())).Succeeded);
        Assert.True((await db.Commands().SaveYearAsync(id, 2026, new ItProjectYearInput(null, "IT PPP 03/2026"), await db.RowVersionAsync(id), ItProjectTestDb.Actor())).Succeeded);

        var edit = await db.Commands().UpdateDetailsAsync(id, ItProjectTestDb.Details(fy: 2025), await db.RowVersionAsync(id), ItProjectTestDb.Actor());

        Assert.True(edit.Succeeded, string.Join("; ", edit.Errors));
        await using var context = db.NewContext();
        var project = await context.ItProjects.Include(p => p.Stages).Include(p => p.FinancialYears).SingleAsync(p => p.Id == id);
        var kept = Assert.Single(project.FinancialYears);
        Assert.Equal(2026, kept.FinancialYearStart);
        Assert.Equal("IT PPP 03/2026", kept.PppReferenceNumber);

        // The new own year is New again, not the CL that was set by hand for it as a later year.
        var own = ItProjectYearTypes.Resolve(project, 2025, 2026)!;
        Assert.True(own.IsOwnYear);
        Assert.False(own.IsManual);
        Assert.Equal(ItProjectYearType.New, own.Type);

        var audit = db.Audit.Entries.Last();
        Assert.Equal("ItProjects.Updated", audit.Action);
        Assert.Equal("2025-26: IT PPP 02/2025, CL set by hand", audit.Data["RemovedYears"]);
    }

    [Fact]
    public async Task UpdateDetails_KeepingTheYear_LeavesTheYearRowsAlone()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync(fy: 2025);
        Assert.True((await db.Commands().SaveYearAsync(id, 2026, new ItProjectYearInput(null, "IT PPP 03/2026"), rowVersion, ItProjectTestDb.Actor())).Succeeded);

        var edit = await db.Commands().UpdateDetailsAsync(id, ItProjectTestDb.Details(title: "Renamed", fy: 2025), await db.RowVersionAsync(id), ItProjectTestDb.Actor());

        Assert.True(edit.Succeeded, string.Join("; ", edit.Errors));
        await using var context = db.NewContext();
        Assert.Equal(1, await context.ItProjectFinancialYears.CountAsync(year => year.ItProjectId == id));
        Assert.False(db.Audit.Entries.Last().Data.ContainsKey("RemovedYears"));
    }

    [Fact]
    public async Task UpdateDetails_WithoutAYearChange_KeepsAnOwnYearTypeSetByHand()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync(fy: 2026);
        Assert.True((await db.Commands().SaveYearAsync(id, 2026, new ItProjectYearInput(ItProjectYearType.CarriedForward, null), rowVersion, ItProjectTestDb.Actor())).Succeeded);

        var edit = await db.Commands().UpdateDetailsAsync(id, ItProjectTestDb.Details(title: "Renamed", fy: 2026), await db.RowVersionAsync(id), ItProjectTestDb.Actor());

        Assert.True(edit.Succeeded, string.Join("; ", edit.Errors));
        await using var context = db.NewContext();
        var row = await context.ItProjectFinancialYears.SingleAsync(year => year.ItProjectId == id);
        Assert.Equal(ItProjectYearType.CarriedForward, row.TypeOverride);
    }

    [Fact]
    public async Task Description_IsLimited_ButAnOlderLongerOneSurvivesAnEditThatLeavesItAlone()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var tooLong = new string('d', ItProjectCommandService.DescriptionMaxLength + 1);

        var (created, _) = await db.Commands().CreateAsync(new ItProjectDetailsInput("LAN", "IT PPP 1/2026", 2026, tooLong), ItProjectTestDb.Actor());
        Assert.False(created.Succeeded);
        Assert.Contains($"The description must be {ItProjectCommandService.DescriptionMaxLength} characters or fewer.", created.Errors);

        // A description saved before the limit existed.
        var (id, _) = await db.SeedAsync();
        await using (var context = db.NewContext())
        {
            var project = await context.ItProjects.SingleAsync(p => p.Id == id);
            project.Description = tooLong;
            await context.SaveChangesAsync();
        }

        var keep = await db.Commands().UpdateDetailsAsync(id, new ItProjectDetailsInput("Renamed", "IT PPP 09/2025", 2025, tooLong), await db.RowVersionAsync(id), ItProjectTestDb.Actor());
        Assert.True(keep.Succeeded, string.Join("; ", keep.Errors));

        var grow = await db.Commands().UpdateDetailsAsync(id, new ItProjectDetailsInput("Renamed", "IT PPP 09/2025", 2025, tooLong + "x"), await db.RowVersionAsync(id), ItProjectTestDb.Actor());
        Assert.False(grow.Succeeded);
    }
}
