using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services.ItProjects;
using Xunit;
using static ProjectManagement.Tests.ItProjects.ItProjectStageRulesTests;

namespace ProjectManagement.Tests.ItProjects;

// New / CL / CF per financial year (docs/ux/it-projects-redesign.md, "Year type"): New in the
// project's own year; in a later year CL when the SO was placed, CF otherwise; changeable by hand.
public class ItProjectYearTypeTests
{
    private const int Current = 2026; // ItProjectTestDb.Now is 8 Nov 2026, FY 2026-27

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static ItProject Project(int fy, params (string Code, DateOnly? Date)[] completed)
    {
        var project = new ItProject { FinancialYearStart = fy, Stages = ItProjectStages.NewStageRows() };
        foreach (var stage in project.Stages)
        {
            var match = completed.FirstOrDefault(item => item.Code == stage.StageCode);
            if (match.Code is not null)
            {
                stage.IsCompleted = true;
                stage.CompletedOn = match.Date;
            }
        }

        // Completing a stage completes the earlier ones (ItProjectStageRules); mirror that here.
        var highest = project.Stages.Where(stage => stage.IsCompleted).Select(stage => stage.SortOrder).DefaultIfEmpty(0).Max();
        foreach (var stage in project.Stages.Where(stage => stage.SortOrder < highest && !stage.IsSkipped))
        {
            stage.IsCompleted = true;
        }

        return project;
    }

    private static string? TypeIn(ItProject project, int fy) => ItProjectYearTypes.Resolve(project, fy, Current)?.Code;

    [Fact]
    public void OwnYear_IsNew()
    {
        Assert.Equal("New", TypeIn(Project(2026), 2026));
        Assert.Equal("New", TypeIn(Project(2025, ("SO", D(2025, 12, 1))), 2025)); // even with the SO placed
    }

    [Fact]
    public void LaterYear_WithoutSo_IsCarriedForward()
    {
        var project = Project(2025, ("TEC", D(2025, 11, 18)));

        Assert.Equal("CF", TypeIn(project, 2026));
        Assert.Equal(new[] { 2025, 2026 }, ItProjectYearTypes.LiveYears(project, Current));
    }

    [Fact]
    public void LaterYear_WithSoPlacedBeforeTheYear_IsCommittedLiability()
        => Assert.Equal("CL", TypeIn(Project(2025, ("SO", D(2026, 1, 8))), 2026));

    [Fact]
    public void SoPlacedDuringTheYear_LeavesThatYearCarriedForward()
    {
        // FY 2024 case, SO placed in Jun 2025: CF in 2025-26 (no SO when it began), CL in 2026-27.
        var project = Project(2024, ("SO", D(2025, 6, 1)));

        Assert.Equal("CF", TypeIn(project, 2025));
        Assert.Equal("CL", TypeIn(project, 2026));
    }

    [Fact]
    public void SoMarkedCompleteWithoutDate_CountsAsPlaced()
        => Assert.Equal("CL", TypeIn(Project(2025, ("SO", null)), 2026));

    [Fact]
    public void DeliveredBeforeTheYear_IsNotCarried()
    {
        var project = Project(2024, ("SO", D(2025, 1, 21)), ("DLV", D(2025, 3, 27)));

        Assert.Equal(new[] { 2024 }, ItProjectYearTypes.LiveYears(project, Current));
        Assert.Null(TypeIn(project, 2025));
    }

    [Fact]
    public void DeliveredDuringTheYear_CountsInThatYear()
    {
        var project = Project(2024, ("SO", D(2025, 2, 24)), ("DLV", D(2025, 6, 30)));

        Assert.Equal(new[] { 2024, 2025 }, ItProjectYearTypes.LiveYears(project, Current));
        Assert.Equal("CL", TypeIn(project, 2025));
    }

    [Fact]
    public void DeliveredWithoutDate_StaysInItsOwnYear()
        => Assert.Equal(new[] { 2024 }, ItProjectYearTypes.LiveYears(Project(2024, ("DLV", null)), Current));

    [Fact]
    public void DroppedBeforeTheYear_IsNotCarried()
    {
        var project = Project(2025, ("BID", D(2025, 10, 15)));
        project.DroppedOn = D(2026, 1, 19);

        Assert.Equal(new[] { 2025 }, ItProjectYearTypes.LiveYears(project, Current));
    }

    [Fact]
    public void FutureYears_AreNotLive()
    {
        Assert.Null(TypeIn(Project(2025), 2027));
        Assert.Equal(new[] { 2027 }, ItProjectYearTypes.LiveYears(Project(2027), Current)); // a next-year case counts in its own year
    }

    [Fact]
    public void ManualType_WinsAndKeepsTheAutomaticOne()
    {
        var project = Project(2025, ("TEC", D(2025, 11, 18)));
        project.FinancialYears.Add(new ItProjectFinancialYear { FinancialYearStart = 2026, TypeOverride = ItProjectYearType.CommittedLiability, PppReferenceNumber = "IT PPP 03/2026" });

        var info = ItProjectYearTypes.Resolve(project, 2026, Current)!;

        Assert.Equal(ItProjectYearType.CommittedLiability, info.Type);
        Assert.Equal(ItProjectYearType.CarriedForward, info.AutomaticType);
        Assert.True(info.IsManual);
        Assert.Equal("IT PPP 03/2026", info.PppReferenceNumber);
        Assert.False(info.IsOwnYear);
        Assert.False(ItProjectYearTypes.Resolve(project, 2025, Current)!.IsManual);
    }

    [Fact]
    public void CurrentFinancialYear_FollowsIst()
    {
        // 31 Mar 2026, 20:00 UTC is 1 Apr 2026, 01:30 IST.
        Assert.Equal(2026, ItProjectYearTypes.CurrentFinancialYear(new DateTimeOffset(2026, 3, 31, 20, 0, 0, TimeSpan.Zero)));
        Assert.Equal(2025, ItProjectYearTypes.CurrentFinancialYear(new DateTimeOffset(2026, 3, 31, 18, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void OwnYear_CarriesTheProjectNumber_LaterYearsTheirOwn()
    {
        var project = Project(2025, ("TEC", D(2025, 11, 18)));
        project.PppReferenceNumber = "IT PPP 09/2025";

        Assert.Equal("IT PPP 09/2025", ItProjectYearTypes.Resolve(project, 2025, Current)!.PppReferenceNumber);
        Assert.Null(ItProjectYearTypes.Resolve(project, 2026, Current)!.PppReferenceNumber); // not entered yet
    }

    // ---- the command ---------------------------------------------------------------------------

    private static ItProjectYearInput Year(ItProjectYearType? type, string? reference = null) => new(type, reference);

    [Fact]
    public async Task SaveYear_StoresNumberAndType_AuditsAndClearRemoves()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync(fy: 2025);
        var stages = await db.Commands().SaveStagesAsync(id, Input(S("TEC", true, D(2025, 11, 18))), rowVersion, ItProjectTestDb.Actor());
        Assert.True(stages.Result.Succeeded);

        var set = await db.Commands().SaveYearAsync(id, 2026, Year(ItProjectYearType.CommittedLiability, "  IT PPP 03/2026 "), await db.RowVersionAsync(id), ItProjectTestDb.Actor("hod-1"));
        Assert.True(set.Succeeded, string.Join("; ", set.Errors));

        var reads = db.Reads();
        var types = reads.GetYearTypes((await reads.GetAsync(id))!);
        Assert.Equal(new[] { "New", "CL" }, types.Select(type => type.Code));
        Assert.Equal(new[] { "IT PPP 09/2025", "IT PPP 03/2026" }, types.Select(type => type.PppReferenceNumber));
        Assert.True(types[1].IsManual);
        Assert.Equal(ItProjectYearType.CarriedForward, types[1].AutomaticType);

        var audit = db.Audit.Entries.Last();
        Assert.Equal("ItProjects.YearChanged", audit.Action);
        Assert.Equal("2026-27", audit.Data!["FinancialYear"]);
        Assert.Equal("CF (automatic)", audit.Data["TypeBefore"]);
        Assert.Equal("CL (set by hand)", audit.Data["TypeAfter"]);
        Assert.Null(audit.Data["ReferenceBefore"]);
        Assert.Equal("IT PPP 03/2026", audit.Data["ReferenceAfter"]);

        // Back to automatic but keep the number: the row stays with the number only.
        var keep = await db.Commands().SaveYearAsync(id, 2026, Year(null, "IT PPP 03/2026"), await db.RowVersionAsync(id), ItProjectTestDb.Actor("hod-1"));
        Assert.True(keep.Succeeded);
        await using (var context = db.NewContext())
        {
            var row = await context.ItProjectFinancialYears.SingleAsync();
            Assert.Null(row.TypeOverride);
            Assert.Equal("IT PPP 03/2026", row.PppReferenceNumber);
        }

        // Clearing both removes the row.
        var clear = await db.Commands().SaveYearAsync(id, 2026, Year(null, " "), await db.RowVersionAsync(id), ItProjectTestDb.Actor("hod-1"));
        Assert.True(clear.Succeeded);
        await using (var context = db.NewContext())
        {
            Assert.False(await context.ItProjectFinancialYears.AnyAsync());
        }
    }

    [Fact]
    public async Task SaveYear_WithNothingChanged_StoresAndAuditsNothing()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync(fy: 2026);
        var auditsBefore = db.Audit.Entries.Count;

        var result = await db.Commands().SaveYearAsync(id, 2026, Year(ItProjectYearType.New), rowVersion, ItProjectTestDb.Actor());

        Assert.True(result.Succeeded);
        Assert.Equal(auditsBefore, db.Audit.Entries.Count);
        await using var context = db.NewContext();
        Assert.False(await context.ItProjectFinancialYears.AnyAsync());
    }

    [Fact]
    public async Task SaveYear_OwnYearNumberIsChangedWithEditOnly()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync(fy: 2026);

        var result = await db.Commands().SaveYearAsync(id, 2026, Year(null, "IT PPP 99/2026"), rowVersion, ItProjectTestDb.Actor());

        Assert.False(result.Succeeded);
        Assert.Contains("Edit", result.Errors.Single());
    }

    [Fact]
    public async Task SaveYear_RejectsAYearTheProjectDoesNotCountIn()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync(fy: 2026);

        var result = await db.Commands().SaveYearAsync(id, 2025, Year(ItProjectYearType.CarriedForward), rowVersion, ItProjectTestDb.Actor());

        Assert.False(result.Succeeded);
        Assert.Contains("FY 2025-26", result.Errors.Single());
    }

    [Fact]
    public async Task SaveYear_RejectsATooLongNumber()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync(fy: 2025);

        var result = await db.Commands().SaveYearAsync(id, 2026, Year(null, new string('x', ItProjectFinancialYear.ReferenceMaxLength + 1)), rowVersion, ItProjectTestDb.Actor());

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SaveYear_StaleRowVersion_IsAConflict()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, staleRowVersion) = await db.SeedAsync(fy: 2025);
        var moved = await db.Commands().SaveStagesAsync(id, Input(S("AON", true)), staleRowVersion, ItProjectTestDb.Actor());
        Assert.True(moved.Result.Succeeded);

        var result = await db.Commands().SaveYearAsync(id, 2026, Year(ItProjectYearType.CommittedLiability), staleRowVersion, ItProjectTestDb.Actor());

        Assert.True(result.ConcurrencyConflict);
    }

    [Fact]
    public async Task BoardSearch_FindsALaterYearNumber()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync(fy: 2025);
        var saved = await db.Commands().SaveYearAsync(id, 2026, Year(null, "IT PPP 03/2026"), rowVersion, ItProjectTestDb.Actor());
        Assert.True(saved.Succeeded);

        var board = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All, Search: "03/2026"));

        Assert.Equal(id, Assert.Single(board.Rows).Id);
    }
}
