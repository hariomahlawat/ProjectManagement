using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Projects;
using ProjectManagement.Services;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// The Add projects filter (docs/ux/briefing-add-projects.md): every chip narrows the list, the
// values inside a chip widen it, a parent category includes its children, ARPP is by financial
// year (main issue and addenda, latest entry not Delisted), completion by financial year.
public sealed class ProjectBriefingProjectFilterTests
{
    private const int Vr = 1;
    private const int VrMedical = 2;
    private const int Driving = 3;
    private const int CoE = 10;
    private const int Other = 11;

    [Fact]
    public async Task NoChips_ListsEveryEligibleProjectByName()
    {
        await using var db = await SeedAsync();

        var rows = await Service(db).FilterAsync(new ProjectBriefingProjectFilter());

        Assert.DoesNotContain(rows, row => row.ProjectName is "Cancelled sim" or "Deleted sim");
        Assert.Equal(rows.Select(row => row.ProjectName).OrderBy(name => name), rows.Select(row => row.ProjectName));
        Assert.Equal(6, rows.Count);
    }

    [Fact]
    public async Task ArppYear_ListsTheMainIssueAndAddenda_LeavingOutTheDelisted()
    {
        await using var db = await SeedAsync();

        var rows = await Service(db).FilterAsync(new ProjectBriefingProjectFilter { ArppFinancialYears = new[] { 2026 } });

        // 101 in the main issue, 102 added by the addendum, 103 delisted by the addendum.
        Assert.Equal(new[] { 101, 102 }, rows.Select(row => row.ProjectId).OrderBy(id => id));
    }

    [Fact]
    public async Task TwoArppYears_ListEitherYear()
    {
        await using var db = await SeedAsync();

        var rows = await Service(db).FilterAsync(new ProjectBriefingProjectFilter { ArppFinancialYears = new[] { 2025, 2026 } });

        Assert.Equal(new[] { 101, 102, 103 }, rows.Select(row => row.ProjectId).OrderBy(id => id));
    }

    [Fact]
    public async Task ParentTechnicalCategory_IncludesItsChildren_AndChipsCombine()
    {
        await using var db = await SeedAsync();
        var service = Service(db);

        var vr = await service.FilterAsync(new ProjectBriefingProjectFilter { TechnicalCategoryIds = new[] { Vr } });
        Assert.Equal(new[] { 101, 103, 104 }, vr.Select(row => row.ProjectId).OrderBy(id => id));

        var vrOrDriving = await service.FilterAsync(new ProjectBriefingProjectFilter { TechnicalCategoryIds = new[] { Vr, Driving } });
        Assert.Equal(new[] { 101, 102, 103, 104 }, vrOrDriving.Select(row => row.ProjectId).OrderBy(id => id));

        // ARPP FY 2026-27 and VR: only 101.
        var both = await service.FilterAsync(new ProjectBriefingProjectFilter
        {
            ArppFinancialYears = new[] { 2026 },
            TechnicalCategoryIds = new[] { Vr }
        });
        Assert.Equal(new[] { 101 }, both.Select(row => row.ProjectId));
    }

    [Fact]
    public async Task ProjectCategoryAndStatus_Filter()
    {
        await using var db = await SeedAsync();
        var service = Service(db);

        var coe = await service.FilterAsync(new ProjectBriefingProjectFilter { ProjectCategoryIds = new[] { CoE } });
        Assert.Equal(new[] { 101, 104, 105 }, coe.Select(row => row.ProjectId).OrderBy(id => id));

        var completedCoe = await service.FilterAsync(new ProjectBriefingProjectFilter
        {
            ProjectCategoryIds = new[] { CoE },
            Statuses = new[] { ProjectLifecycleStatus.Completed }
        });
        Assert.Equal(new[] { 104, 105 }, completedCoe.Select(row => row.ProjectId).OrderBy(id => id));
    }

    [Fact]
    public async Task CompletedBetween_UsesFinancialYears()
    {
        await using var db = await SeedAsync();
        var service = Service(db);

        // 104 completed 15 Feb 2025 (FY 2024-25), 105 on 10 May 2025 (FY 2025-26), 106 in the
        // calendar year 2023 with no month (FY 2022-23 or 2023-24).
        var fy2025 = await service.FilterAsync(new ProjectBriefingProjectFilter { CompletedFromFinancialYear = 2025, CompletedToFinancialYear = 2025 });
        Assert.Equal(new[] { 105 }, fy2025.Select(row => row.ProjectId));

        var upTo2024 = await service.FilterAsync(new ProjectBriefingProjectFilter { CompletedToFinancialYear = 2024 });
        Assert.Equal(new[] { 104, 106 }, upTo2024.Select(row => row.ProjectId).OrderBy(id => id));

        // A reversed range is read the right way round.
        var reversed = await service.FilterAsync(new ProjectBriefingProjectFilter { CompletedFromFinancialYear = 2025, CompletedToFinancialYear = 2023 });
        Assert.Equal(new[] { 104, 105, 106 }, reversed.Select(row => row.ProjectId).OrderBy(id => id));
    }

    [Fact]
    public async Task Proliferation_ListsProjectsMarkedAvailable()
    {
        await using var db = await SeedAsync();

        var rows = await Service(db).FilterAsync(new ProjectBriefingProjectFilter { AvailableForProliferation = true });

        Assert.Equal(new[] { 105 }, rows.Select(row => row.ProjectId));
    }

    [Fact]
    public async Task Options_ListArppYearsLatestFirstWithTheirProjects_AndCompletionFinancialYears()
    {
        await using var db = await SeedAsync();

        var options = await Service(db).GetOptionsAsync();

        Assert.Equal(new[] { 2026, 2025 }, options.ArppYears.Select(year => year.FinancialYearStart));
        Assert.Equal("FY 2026-27", options.ArppYears[0].Label);
        Assert.Equal(2, options.ArppYears[0].ProjectCount);
        Assert.Equal(1, options.ArppYears[1].ProjectCount);
        // 106 has only the calendar year 2023: FY 2022-23 or FY 2023-24.
        Assert.Equal(2022, options.MinimumCompletionFinancialYear);
        Assert.Equal(2025, options.MaximumCompletionFinancialYear);
    }

    [Fact]
    public async Task Options_CountOnlyProjectsTheFilterCanReturn()
    {
        await using var db = await SeedAsync();
        // A project the FY 2026-27 ARPP lists is then cancelled (Codex review).
        db.Projects.Single(project => project.Id == 101).LifecycleStatus = ProjectLifecycleStatus.Cancelled;
        await db.SaveChangesAsync();
        var service = Service(db);

        var options = await service.GetOptionsAsync();
        var rows = await service.FilterAsync(new ProjectBriefingProjectFilter { ArppFinancialYears = new[] { 2026 } });

        Assert.Equal(1, options.ArppYears.Single(year => year.FinancialYearStart == 2026).ProjectCount);
        Assert.Equal(new[] { 102 }, rows.Select(row => row.ProjectId));
    }

    [Fact]
    public async Task Filter_ListsEveryMatch_WithNoLimit()
    {
        await using var db = await SeedAsync();
        db.Projects.AddRange(Enumerable.Range(1000, 1100).Select(id => new Project
        {
            Id = id,
            Name = $"Bulk {id}",
            CreatedByUserId = "u",
            LifecycleStatus = ProjectLifecycleStatus.Active
        }));
        await db.SaveChangesAsync();

        var rows = await Service(db).FilterAsync(new ProjectBriefingProjectFilter());

        Assert.Equal(1106, rows.Count);
    }

    // The dialog's read (cold review 6): every match's id, by name, and full rows for only the
    // first few, the same first few the whole list would start with.
    [Fact]
    public async Task FilterPage_CountsEveryMatch_ButBuildsRowsForTheFirstOnly()
    {
        await using var db = await SeedAsync();
        db.Projects.AddRange(Enumerable.Range(1000, 300).Select(id => new Project
        {
            Id = id,
            Name = $"Bulk {id}",
            CreatedByUserId = "u",
            LifecycleStatus = ProjectLifecycleStatus.Active
        }));
        await db.SaveChangesAsync();
        var service = Service(db);

        var page = await service.FilterPageAsync(new ProjectBriefingProjectFilter(), take: 200);
        var all = await service.FilterAsync(new ProjectBriefingProjectFilter());

        Assert.Equal(306, page.ProjectIds.Count);
        Assert.Equal(200, page.Rows.Count);
        Assert.Equal(all.Select(row => row.ProjectId), page.ProjectIds);
        Assert.Equal(all.Take(200).Select(row => row.ProjectId), page.Rows.Select(row => row.ProjectId));
    }

    [Fact]
    public async Task ArppYear_AProjectDelistedThenRelistedInTheSameYearIsListed()
    {
        await using var db = await SeedAsync();
        // 103 was delisted by the FY 2026-27 addendum; a later addendum lists it again.
        db.ArppPublishedIssues.Add(new ArppPublishedIssue { ArppIssueId = 4, FinancialYearStart = 2026, IssueSequence = 2, IssueDate = new DateOnly(2027, 1, 20), Name = "Issue 4" });
        db.ArppPublishedEntries.Add(new ArppPublishedEntry { Id = 99, ArppIssueId = 4, ProjectId = 103, Category = ArppCategory.New, ProjectReference = "P103" });
        await db.SaveChangesAsync();

        var rows = await Service(db).FilterAsync(new ProjectBriefingProjectFilter { ArppFinancialYears = new[] { 2026 } });

        Assert.Equal(new[] { 101, 102, 103 }, rows.Select(row => row.ProjectId).OrderBy(id => id));
    }

    [Theory]
    // A completion recorded as month and year only: January to March belong to the year before.
    [InlineData((short)2, 2024)]
    [InlineData((short)4, 2025)]
    public async Task CompletedBetween_UsesTheRecordedMonthWhenThereIsNoDate(short month, int financialYear)
    {
        await using var db = await SeedAsync();
        var project = db.Projects.Single(candidate => candidate.Id == 106);
        project.CompletedYear = 2025;
        project.CompletedMonth = month;
        await db.SaveChangesAsync();

        var rows = await Service(db).FilterAsync(new ProjectBriefingProjectFilter { CompletedFromFinancialYear = financialYear, CompletedToFinancialYear = financialYear });

        Assert.Contains(rows, row => row.ProjectId == 106);
    }

    // CompletedYear is a calendar year (owner, 19 Nov 2026): with no month it may be either of the
    // two financial years it spans, and matches both, but not the years beyond.
    [Theory]
    [InlineData(2022, true)]
    [InlineData(2023, true)]
    [InlineData(2021, false)]
    [InlineData(2024, false)]
    public async Task CompletedBetween_AYearWithNoMonthMatchesBothFinancialYearsItSpans(int financialYear, bool matched)
    {
        await using var db = await SeedAsync();

        var rows = await Service(db).FilterAsync(new ProjectBriefingProjectFilter { CompletedFromFinancialYear = financialYear, CompletedToFinancialYear = financialYear });

        Assert.Equal(matched, rows.Any(row => row.ProjectId == 106));
    }

    [Fact]
    public async Task Options_ListEachCategoryFollowedByItsSubCategories()
    {
        await using var db = await SeedAsync();
        // SortOrder counts within each parent, as the admin pages number it: a flat sort would
        // put "CoE Short Term" (2) after "Other R&D" (1).
        db.ProjectCategories.AddRange(
            new ProjectCategory { Id = 20, Name = "CoE Long Term", ParentId = CoE, SortOrder = 0, IsActive = true },
            new ProjectCategory { Id = 21, Name = "CoE Mid Term", ParentId = CoE, SortOrder = 1, IsActive = true },
            new ProjectCategory { Id = 22, Name = "CoE Short Term", ParentId = CoE, SortOrder = 2, IsActive = true });
        db.ProjectCategories.Single(category => category.Id == Other).SortOrder = 1;
        await db.SaveChangesAsync();

        var options = await Service(db).GetOptionsAsync();
        var categories = await Service(db).GetCategoryOptionsAsync();

        Assert.Equal(new[] { "CoE", "CoE Long Term", "CoE Mid Term", "CoE Short Term", "Other R&D" }, options.ProjectCategories.Select(category => category.Name));
        Assert.Equal(new[] { 0, 1, 1, 1, 0 }, options.ProjectCategories.Select(category => category.Depth));
        // The page's light list has the same order, with no counts.
        Assert.Equal(options.ProjectCategories.Select(category => category.Name), categories.ProjectCategories.Select(category => category.Name));
        Assert.All(categories.ProjectCategories, category => Assert.Equal(0, category.MatchCount));
    }

    // An inactive sub-category is not offered, but its projects count towards its parent, as the
    // parent's filter returns them (cold review 3, M5).
    [Fact]
    public async Task Options_AParentCountsTheProjectsOfItsInactiveSubCategories()
    {
        await using var db = await SeedAsync();
        db.TechnicalCategories.Single(category => category.Id == VrMedical).IsActive = false;
        await db.SaveChangesAsync();

        var options = await Service(db).GetOptionsAsync();
        var rows = await Service(db).FilterAsync(new ProjectBriefingProjectFilter { TechnicalCategoryIds = new[] { Vr } });

        Assert.DoesNotContain(options.TechnicalCategories, category => category.Name == "VR medical");
        Assert.Equal(rows.Count, options.TechnicalCategories.Single(category => category.Name == "VR").MatchCount);
    }

    // Only the years some project was completed in are offered (cold review 5): one stray old year
    // does not bring every year between.
    [Fact]
    public async Task Options_ListOnlyTheCompletionYearsInUse()
    {
        await using var db = await SeedAsync();
        db.Projects.Single(project => project.Id == 106).CompletedYear = 1990;
        await db.SaveChangesAsync();

        var options = await Service(db).GetOptionsAsync();

        // 1990 alone, a calendar year, may be FY 1989-90 or FY 1990-91.
        Assert.Equal(new[] { 2025, 2024, 1990, 1989 }, options.CompletionFinancialYears);
    }

    // "%" and "_" typed in the search box are matched as themselves (cold review 3).
    [Theory]
    [InlineData("50%", @"%50\%%")]
    [InlineData("a_b", @"%a\_b%")]
    [InlineData(@"c:\x", @"%c:\\x%")]
    [InlineData("plain", "%plain%")]
    public void Search_EscapesTheLikeWildcards(string typed, string expected)
        => Assert.Equal(expected, ProjectBriefingSelectionService.ContainsPattern(typed));

    [Theory]
    [InlineData(ProjectBriefingSelectionKind.Ongoing)]
    [InlineData(ProjectBriefingSelectionKind.RecentlyCompleted)]
    [InlineData(ProjectBriefingSelectionKind.MyProjects)]
    public async Task Resolve_RefusesTheOldWaysOfAdding(ProjectBriefingSelectionKind kind)
    {
        await using var db = await SeedAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).ResolveAsync(new ProjectBriefingSelectionRequest { Kind = kind, OfficerUserId = "u" }));

        Assert.Contains("no longer available", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_AddsTheTickedProjects()
    {
        await using var db = await SeedAsync();

        var result = await Service(db).ResolveAsync(new ProjectBriefingSelectionRequest
        {
            Kind = ProjectBriefingSelectionKind.IndividualProjects,
            ProjectIds = new[] { 101, 107, 105 }
        });

        // 107 is cancelled, so it cannot be added.
        Assert.Equal(new[] { 101, 105 }, result.ProjectIds.OrderBy(id => id));
    }

    [Fact]
    public async Task Options_CountTheCompletedProjectsWithNoCompletionDate()
    {
        await using var db = await SeedAsync();
        // 106 was completed with a year; with neither a date nor a year, no range can match it.
        db.Projects.Single(project => project.Id == 106).CompletedYear = null;
        await db.SaveChangesAsync();

        var options = await Service(db).GetOptionsAsync();

        Assert.Equal(1, options.CompletedWithoutDateCount);
    }

    [Theory]
    // A deck holds 120: the message says how many more fit and how many to leave out.
    [InlineData(40, 167, "room for 80 more projects (a deck holds up to 120). Leave out 87")]
    [InlineData(119, 3, "room for 1 more project (a deck holds up to 120). Leave out 2")]
    [InlineData(120, 1, "already holds 120 projects")]
    public void TooManyProjects_SaysHowManyMoreFit(int inDeck, int adding, string expected)
        => Assert.Contains(expected, ProjectBriefingDeckService.TooManyProjectsMessage(inDeck, adding), StringComparison.Ordinal);

    [Theory]
    [InlineData(2026, 3, 31, 2025)]
    [InlineData(2026, 4, 1, 2026)]
    [InlineData(2026, 12, 31, 2026)]
    public void FinancialYearOf_StartsInApril(int year, int month, int day, int expected)
        => Assert.Equal(expected, ProjectBriefingSelectionService.FinancialYearOf(new DateOnly(year, month, day)));

    private static ProjectBriefingSelectionService Service(ApplicationDbContext db) => new(db, new Clock());

    private static async Task<ApplicationDbContext> SeedAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"briefing-filter-{Guid.NewGuid()}")
            .Options);

        db.TechnicalCategories.AddRange(
            new TechnicalCategory { Id = Vr, Name = "VR", IsActive = true },
            new TechnicalCategory { Id = VrMedical, Name = "VR medical", ParentId = Vr, IsActive = true },
            new TechnicalCategory { Id = Driving, Name = "Driving", IsActive = true });
        db.ProjectCategories.AddRange(
            new ProjectCategory { Id = CoE, Name = "CoE", IsActive = true },
            new ProjectCategory { Id = Other, Name = "Other R&D", IsActive = true });

        Project ProjectRow(int id, string name, ProjectLifecycleStatus status, int? technical, int? category)
            => new() { Id = id, Name = name, CreatedByUserId = "u", LifecycleStatus = status, TechnicalCategoryId = technical, CategoryId = category };

        db.Projects.AddRange(
            ProjectRow(101, "Alpha VR", ProjectLifecycleStatus.Active, Vr, CoE),
            ProjectRow(102, "Bravo driving", ProjectLifecycleStatus.Active, Driving, Other),
            ProjectRow(103, "Charlie medical", ProjectLifecycleStatus.Active, VrMedical, Other),
            ProjectRow(104, "Delta VR", ProjectLifecycleStatus.Completed, Vr, CoE),
            ProjectRow(105, "Echo", ProjectLifecycleStatus.Completed, null, CoE),
            ProjectRow(106, "Foxtrot", ProjectLifecycleStatus.Completed, null, null),
            ProjectRow(107, "Cancelled sim", ProjectLifecycleStatus.Cancelled, Vr, CoE));
        var deleted = ProjectRow(108, "Deleted sim", ProjectLifecycleStatus.Active, Vr, CoE);
        deleted.IsDeleted = true;
        db.Projects.Add(deleted);

        var projects = db.Projects.Local;
        projects.Single(project => project.Id == 104).CompletedOn = new DateOnly(2025, 2, 15);
        projects.Single(project => project.Id == 105).CompletedOn = new DateOnly(2025, 5, 10);
        projects.Single(project => project.Id == 106).CompletedYear = 2023;

        db.ProjectTechStatuses.Add(new ProjectTechStatus { ProjectId = 105, AvailableForProliferation = true });
        db.ProjectTechStatuses.Add(new ProjectTechStatus { ProjectId = 104, AvailableForProliferation = false });

        // FY 2025-26: 103. FY 2026-27: main issue 101 and 103; addendum adds 102 and delists 103.
        ArppPublishedIssue Issue(long id, int year, int sequence, DateOnly date)
            => new() { ArppIssueId = id, FinancialYearStart = year, IssueSequence = sequence, IssueDate = date, Name = $"Issue {id}" };
        db.ArppPublishedIssues.AddRange(
            Issue(1, 2025, 0, new DateOnly(2025, 4, 20)),
            Issue(2, 2026, 0, new DateOnly(2026, 4, 20)),
            Issue(3, 2026, 1, new DateOnly(2026, 9, 15)));
        var entryId = 0L;
        ArppPublishedEntry Entry(long issue, int project, ArppCategory category)
            => new() { Id = ++entryId, ArppIssueId = issue, ProjectId = project, Category = category, ProjectReference = $"P{project}" };
        db.ArppPublishedEntries.AddRange(
            Entry(1, 103, ArppCategory.New),
            Entry(2, 101, ArppCategory.New),
            Entry(2, 103, ArppCategory.CarryForward),
            Entry(3, 102, ArppCategory.New),
            Entry(3, 103, ArppCategory.Delisted));

        await db.SaveChangesAsync();
        return db;
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 17, 6, 0, 0, TimeSpan.Zero);
    }
}
