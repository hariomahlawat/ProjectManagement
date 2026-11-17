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

        // 104 completed 15 Feb 2025 (FY 2024-25), 105 on 10 May 2025 (FY 2025-26), 106 in 2023
        // with no month (FY 2023-24).
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
        Assert.Equal(2023, options.MinimumCompletionFinancialYear);
        Assert.Equal(2025, options.MaximumCompletionFinancialYear);
    }

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
