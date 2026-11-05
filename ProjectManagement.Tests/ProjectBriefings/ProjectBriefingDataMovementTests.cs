using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// "Data moved since last generated": the facts a generation keeps, and how the library compares
// them with what the same resolvers say today.
public sealed class ProjectBriefingDataMovementTests
{
    private static readonly DateOnly RemarkDate = new(2026, 10, 30);

    private const ProjectBriefingShownFacts All =
        ProjectBriefingShownFacts.Stage | ProjectBriefingShownFacts.Status | ProjectBriefingShownFacts.CostRd | ProjectBriefingShownFacts.CostRdBasis
        | ProjectBriefingShownFacts.IpaCost | ProjectBriefingShownFacts.ProliferationCost;

    [Fact]
    public void FactsKeptAtGeneration_MatchTodaysFactsForUnchangedData()
    {
        // Generation reads the presentation project (with the slide's "no status" text); the
        // library reads current facts. The same data must give the same fact.
        var presented = new ProjectBriefingPresentationProject
        {
            ProjectId = 7,
            LifecycleStatus = ProjectLifecycleStatus.Active,
            PresentStageCode = "devp",
            PresentStageOrder = ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, "DEVP"),
            ExternalStatus = ProjectBriefingSlideFacts.NoExternalStatus,
            CostRd = Cost(1_250_000.4m),
            IpaCost = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.IPA),
            ProliferationCost = Cost(300_000m)
        };
        var data = Data(ProjectBriefingPresentationMode.Combined, ProjectBriefingCostMode.Both, presented);
        var current = new ProjectBriefingCurrentProjectFacts(
            7, "Counter-UAS", ProjectLifecycleStatus.Active, "DEVP", null, null,
            Cost(1_250_000m), ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.IPA), Cost(300_000m));

        var kept = ProjectBriefingSlideFacts.FromPresentation(presented, data);

        Assert.Equal(kept, current.ToSlideFact(kept.Shown));
    }

    [Fact]
    public void Compare_CountsStageStatusAndCostSeparately()
    {
        var generated = new[]
        {
            Fact(1, "DEVP", "On track", 100m),
            Fact(2, "AON", "Trials", 200m),
            Fact(3, "SO", null, 300m),
            Fact(4, "DEVP", "Unchanged", 400m)
        };
        var current = new Dictionary<int, ProjectBriefingCurrentProjectFacts>
        {
            [1] = Current(1, "COMPLETED", "On track", 100m),
            [2] = Current(2, "AON", "Trials complete", 250m),
            [3] = Current(3, "SO", null, 300m),
            [4] = Current(4, "DEVP", "Unchanged", 400m)
        };

        var movement = ProjectBriefingSlideFacts.Compare(Generated(generated), current);

        Assert.Equal(1, movement.StageChanges);
        Assert.Equal(1, movement.StatusChanges);
        Assert.Equal(1, movement.CostChanges);
        Assert.Equal(3, movement.Total);
        Assert.Equal(new[] { 1, 2 }, movement.Projects.Select(project => project.ProjectId));
        Assert.Equal(
            new[] { "1 project changed stage", "1 new status remark", "1 cost revised" },
            movement.Describe());
    }

    [Fact]
    public void Compare_LeavesOutProjectsThatAreGone()
    {
        var current = new Dictionary<int, ProjectBriefingCurrentProjectFacts> { [1] = Current(1, "DEVP", "x", 100m) };

        var movement = ProjectBriefingSlideFacts.Compare(Generated(Fact(1, "DEVP", "x", 100m), Fact(9, "DEVP", "deleted since", 1m)), current);

        Assert.False(movement.HasMoved);
    }

    [Fact]
    public void Compare_IgnoresFactsTheGeneratedSlidesDidNotShow()
    {
        var before = Current(1, "DEVP", "x", 100m);
        var after = Current(1, "AON", "y", 500m) with { IpaCost = Cost(11m), ProliferationCost = Cost(21m) };
        var current = new Dictionary<int, ProjectBriefingCurrentProjectFacts> { [1] = after };

        Assert.False(ProjectBriefingSlideFacts.Compare(Generated(before.ToSlideFact(ProjectBriefingShownFacts.None)), current).HasMoved);

        var proliferationOnly = ProjectBriefingSlideFacts.Compare(Generated(before.ToSlideFact(ProjectBriefingShownFacts.ProliferationCost)), current);
        Assert.Equal((0, 0, 1), (proliferationOnly.StageChanges, proliferationOnly.StatusChanges, proliferationOnly.CostChanges));
    }

    [Fact]
    public void Compare_CountsAChangeOfCostBasisAtTheSameAmount()
    {
        var before = Current(1, "DEVP", "x", 100m) with { CostRd = new ProjectBriefingCostValue(100m, ProjectBriefingCostBasis.None, "1.00", "L1") };
        var after = before with { CostRd = new ProjectBriefingCostValue(100m, ProjectBriefingCostBasis.None, "1.00", "PNC") };

        var movement = ProjectBriefingSlideFacts.Compare(
            Generated(before.ToSlideFact(ProjectBriefingShownFacts.CostRd | ProjectBriefingShownFacts.CostRdBasis)),
            new Dictionary<int, ProjectBriefingCurrentProjectFacts> { [1] = after });

        Assert.Equal(1, movement.CostChanges);

        // An update sheet's cost row and the portfolio totals show the amount only.
        var amountOnly = ProjectBriefingSlideFacts.Compare(
            Generated(before.ToSlideFact(ProjectBriefingShownFacts.CostRd)),
            new Dictionary<int, ProjectBriefingCurrentProjectFacts> { [1] = after });
        Assert.Equal(0, amountOnly.CostChanges);
    }

    [Fact]
    public void Compare_StageOrderOnlyCountsAMoveToAnotherMaturity()
    {
        var before = Current(1, "DEVP", "x", 1m);
        var kept = Generated(before.ToSlideFact(ProjectBriefingShownFacts.StageOrder));

        var completed = ProjectBriefingSlideFacts.Compare(kept, new Dictionary<int, ProjectBriefingCurrentProjectFacts>
        {
            [1] = before with { LifecycleStatus = ProjectLifecycleStatus.Completed, StageCode = ProjectBriefingStageOrder.CompletedCode }
        });

        Assert.Equal(1, completed.StageChanges);
    }

    [Theory]
    // The portfolio summary counts lifecycle: moving between ongoing stages is not drawn...
    [InlineData(ProjectBriefingShownFacts.Lifecycle, "EAS", ProjectLifecycleStatus.Active, false)]
    // ...completing is.
    [InlineData(ProjectBriefingShownFacts.Lifecycle, "COMPLETED", ProjectLifecycleStatus.Completed, true)]
    // Cancelling keeps the stage; the lifecycle reading still changes.
    [InlineData(ProjectBriefingShownFacts.Lifecycle, "AON", ProjectLifecycleStatus.Cancelled, true)]
    // The update-sheet milestone row: two ongoing stages outside development read the same...
    [InlineData(ProjectBriefingShownFacts.Milestone, "EAS", ProjectLifecycleStatus.Active, false)]
    // ...and entering development with no PDC date recorded draws the same blank PDC row.
    [InlineData(ProjectBriefingShownFacts.Milestone, "DEVP", ProjectLifecycleStatus.Active, false)]
    [InlineData(ProjectBriefingShownFacts.Milestone, "AON", ProjectLifecycleStatus.Cancelled, true)]
    public void Compare_StageCountsOnlyAsTheSlidesReadIt(
        ProjectBriefingShownFacts shown,
        string stageNow,
        ProjectLifecycleStatus lifecycleNow,
        bool counted)
    {
        var before = Current(1, "AON", "x", 1m);
        var after = before with { StageCode = stageNow, LifecycleStatus = lifecycleNow };

        var movement = ProjectBriefingSlideFacts.Compare(
            Generated(before.ToSlideFact(shown)),
            new Dictionary<int, ProjectBriefingCurrentProjectFacts> { [1] = after });

        Assert.Equal(counted ? 1 : 0, movement.StageChanges);
    }

    [Fact]
    public void Compare_MilestoneRowCountsTheValueTheUpdateSheetDraws()
    {
        var inDevelopment = Current(1, "DEVP", "x", 1m) with { DevelopmentPdcDate = new DateOnly(2027, 3, 12) };
        var completed = Current(2, "COMPLETED", "x", 1m) with
        {
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            CompletionStatusDisplay = "Project completed in Mar 2026"
        };
        var kept = Generated(
            inDevelopment.ToSlideFact(ProjectBriefingShownFacts.Milestone),
            completed.ToSlideFact(ProjectBriefingShownFacts.Milestone));

        var movement = ProjectBriefingSlideFacts.Compare(kept, new Dictionary<int, ProjectBriefingCurrentProjectFacts>
        {
            [1] = inDevelopment with { DevelopmentPdcDate = new DateOnly(2027, 6, 30) },
            [2] = completed with { CompletionStatusDisplay = "Project completed on 14 Mar 2026" }
        });

        Assert.Equal(2, movement.StageChanges);
        Assert.Contains("PDC Date: 12 Mar 27", kept.Facts[0].Stage);
    }

    [Theory]
    [InlineData(ProjectBriefingNarrativeMode.CapabilityOverview, "Short brief.", true)]
    [InlineData(ProjectBriefingNarrativeMode.ProjectBrief, "Short brief.", false)]
    public void Shown_LifecycleFollowsTheSlidesThatDrawTheSubtitle(ProjectBriefingNarrativeMode narrative, string brief, bool lifecycleShown)
    {
        var project = new ProjectBriefingPresentationProject { ProjectId = 1, ProjectBrief = brief };
        var data = new ProjectBriefingPresentationData
        {
            Layout = ProjectBriefingLayout.StandardBriefing,
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            NarrativeMode = narrative,
            CostMode = ProjectBriefingCostMode.None,
            IncludePortfolioSummarySlide = false,
            Projects = new[] { project }
        };

        Assert.Equal(lifecycleShown, ProjectBriefingSlideFacts.Shown(project, data).HasFlag(ProjectBriefingShownFacts.Lifecycle));
    }

    [Fact]
    public void Shown_LifecycleUsesTheDecksOwnBriefPagination()
    {
        // Fits the standard brief panel, but continues in the narrower photo-emphasis panel, whose
        // continuation slide draws the lifecycle subtitle.
        var brief = string.Concat(Enumerable.Repeat("alpha ", 300)).Trim();
        var project = new ProjectBriefingPresentationProject { ProjectId = 1, ProjectBrief = brief, ExternalStatus = "Short." };
        var data = new ProjectBriefingPresentationData
        {
            Layout = ProjectBriefingLayout.StandardBriefing,
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            NarrativeMode = ProjectBriefingNarrativeMode.ProjectBrief,
            CostMode = ProjectBriefingCostMode.None,
            IncludePortfolioSummarySlide = false,
            StandardSlideOptions = ProjectBriefingStandardSlideOptions.Default with
            {
                ProjectBriefLayout = ProjectBriefingProjectBriefLayout.PhotoEmphasis,
                ShowPresentStage = false
            },
            Projects = new[] { project }
        };

        Assert.Equal(1, ProjectBriefingSlideComposer.EstimateProjectBriefSlideCount(brief));
        Assert.True(ProjectBriefingSlideComposer.ProjectBriefSlideCount(data, project) > 1);
        Assert.True(ProjectBriefingSlideFacts.Shown(project, data).HasFlag(ProjectBriefingShownFacts.Lifecycle));
    }

    [Fact]
    public void Shown_PortfolioSummaryTracksLifecycleAndTheUpdateSheetItsMilestone()
    {
        var project = new ProjectBriefingPresentationProject { ProjectId = 1 };
        var standard = new ProjectBriefingPresentationData
        {
            Layout = ProjectBriefingLayout.StandardBriefing,
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            CostMode = ProjectBriefingCostMode.None,
            IncludePortfolioSummarySlide = true,
            StandardSlideOptions = ProjectBriefingStandardSlideOptions.Default with { ShowPresentStage = false, ShowPresentStatus = false },
            Projects = new[] { project }
        };
        var sheet = new ProjectBriefingPresentationData
        {
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            CostMode = ProjectBriefingCostMode.CostRdOnly,
            IncludePortfolioSummarySlide = false,
            UpdateSheetOptions = new ProjectBriefingUpdateSheetOptions(new[] { ProjectBriefingUpdateSheetRow.PdcOrCompletionStatus }, HideEmptyValues: false),
            Projects = new[] { project }
        };

        Assert.Equal(ProjectBriefingShownFacts.Lifecycle, ProjectBriefingSlideFacts.Shown(project, standard));
        // Without the portfolio summary, a detail slide's subtitle still draws the lifecycle, so
        // cancelling a project (its stages kept) counts.
        Assert.Equal(ProjectBriefingShownFacts.Lifecycle, ProjectBriefingSlideFacts.Shown(project, new ProjectBriefingPresentationData
        {
            Layout = standard.Layout,
            PresentationMode = standard.PresentationMode,
            CostMode = standard.CostMode,
            IncludePortfolioSummarySlide = false,
            StandardSlideOptions = standard.StandardSlideOptions,
            Projects = standard.Projects
        }));
        Assert.Equal(ProjectBriefingShownFacts.Milestone, ProjectBriefingSlideFacts.Shown(project, sheet));

        // The stage summary's status band and ongoing chart count lifecycle too, so cancelling a
        // project in an executive-table deck counts.
        var tableWithStageSummary = ProjectBriefingSlideFacts.Shown(project, new ProjectBriefingPresentationData
        {
            Layout = ProjectBriefingLayout.StandardBriefing,
            PresentationMode = ProjectBriefingPresentationMode.ExecutiveTable,
            CostMode = ProjectBriefingCostMode.None,
            IncludePortfolioSummarySlide = false,
            IncludeStageSummary = true,
            Projects = new[] { project }
        });
        Assert.True(tableWithStageSummary.HasFlag(ProjectBriefingShownFacts.Lifecycle));
    }

    // Where the composer draws each fact (ProjectBriefingSlideFacts.Shown).
    [Theory]
    // Detailed slides with stage and status hidden, one project, no summaries: neither is shown.
    [InlineData(ProjectBriefingLayout.StandardBriefing, ProjectBriefingPresentationMode.DetailedProjects, false, false, false, false, false, false)]
    // ...but a table-row-only project is in the executive table.
    [InlineData(ProjectBriefingLayout.StandardBriefing, ProjectBriefingPresentationMode.DetailedProjects, false, false, false, true, true, true)]
    // Detailed slides showing stage and status.
    [InlineData(ProjectBriefingLayout.StandardBriefing, ProjectBriefingPresentationMode.DetailedProjects, true, true, false, false, true, true)]
    // The executive table always shows both.
    [InlineData(ProjectBriefingLayout.StandardBriefing, ProjectBriefingPresentationMode.ExecutiveTable, false, false, false, false, true, true)]
    // The stage summary shows the stage.
    [InlineData(ProjectBriefingLayout.StandardBriefing, ProjectBriefingPresentationMode.DetailedProjects, false, false, true, false, true, false)]
    public void Shown_FollowsTheComposer(
        ProjectBriefingLayout layout,
        ProjectBriefingPresentationMode mode,
        bool showStage,
        bool showStatus,
        bool stageSummary,
        bool tableRowOnly,
        bool stageShown,
        bool statusShown)
    {
        var project = new ProjectBriefingPresentationProject { ProjectId = 1, TableRowOnly = tableRowOnly };
        var data = new ProjectBriefingPresentationData
        {
            Layout = layout,
            PresentationMode = mode,
            CostMode = ProjectBriefingCostMode.None,
            IncludeStageSummary = stageSummary,
            IncludePortfolioSummarySlide = false,
            StandardSlideOptions = ProjectBriefingStandardSlideOptions.Default with { ShowPresentStage = showStage, ShowPresentStatus = showStatus },
            Projects = new[] { project }
        };

        var shown = ProjectBriefingSlideFacts.Shown(project, data);

        Assert.Equal(stageShown, shown.HasFlag(ProjectBriefingShownFacts.Stage));
        Assert.Equal(statusShown, shown.HasFlag(ProjectBriefingShownFacts.Status));
        Assert.False(shown.HasFlag(ProjectBriefingShownFacts.CostRd));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Shown_UpdateSheetFollowsItsSelectedRows(bool statusRow, bool statusShown)
    {
        var project = new ProjectBriefingPresentationProject { ProjectId = 1 };
        var rows = statusRow
            ? new[] { ProjectBriefingUpdateSheetRow.PresentStatus }
            : new[] { ProjectBriefingUpdateSheetRow.AonDate };
        var data = new ProjectBriefingPresentationData
        {
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            CostMode = ProjectBriefingCostMode.CostRdOnly,
            UpdateSheetOptions = new ProjectBriefingUpdateSheetOptions(rows, HideEmptyValues: false),
            IncludePortfolioSummarySlide = false,
            Projects = new[] { project }
        };

        var shown = ProjectBriefingSlideFacts.Shown(project, data);

        Assert.Equal(statusShown, shown.HasFlag(ProjectBriefingShownFacts.Status));
        Assert.False(shown.HasFlag(ProjectBriefingShownFacts.CostRd));
        Assert.False(shown.HasFlag(ProjectBriefingShownFacts.Stage));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void Read_ReturnsNullWhenNothingUsableWasKept(string? json)
        => Assert.Null(ProjectBriefingSlideFacts.Read(json));

    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        var facts = new[] { Fact(2, "AON", "b", 2m), Fact(1, "DEVP", "a", 1m) };

        var read = ProjectBriefingSlideFacts.Read(ProjectBriefingSlideFacts.Write(ProjectBriefingLayout.ProjectUpdateSheet, ProjectBriefingCostMode.CostRdOnly, facts));

        Assert.NotNull(read);
        Assert.Equal((ProjectBriefingLayout.ProjectUpdateSheet, ProjectBriefingCostMode.CostRdOnly), (read!.Layout, read.CostMode));
        Assert.Equal(facts.OrderBy(fact => fact.ProjectId), read.Facts);
    }

    [Fact]
    public async Task Library_ShowsMovementFromTheSameResolversAsTheSlides()
    {
        await using var db = CreateDb();
        db.Projects.AddRange(
            new Project { Id = 1, Name = "Counter-UAS System", CreatedByUserId = "u" },
            new Project { Id = 2, Name = "VR Based Map Reading", CreatedByUserId = "u" });
        await db.SaveChangesAsync();
        var costs = new FakeCosts();
        var statuses = new FakeStatuses();
        costs.CostRd[1] = Cost(100m);
        costs.CostRd[2] = Cost(200m);
        statuses.Latest[2] = new ProjectBriefingExternalStatus(2, 11, "Trials planned", RemarkDate, DateTime.UtcNow);
        var service = Service(db, costs, statuses);

        // What a generation showing stage, status and R&D cost would have kept, today.
        var kept = (await service.GetCurrentSlideFactsAsync(new[] { 1, 2 }))
            .Values.Select(facts => facts.ToSlideFact(ProjectBriefingShownFacts.Stage | ProjectBriefingShownFacts.Status | ProjectBriefingShownFacts.CostRd))
            .ToArray();
        // Costs were switched off after generating: a settings edit, not moved data, so the R&D
        // revision below still counts against the generated slides.
        var generatedDeck = Summary(1, "Monthly review", ProjectBriefingCostMode.None) with
        {
            LastGeneratedAtUtc = DateTimeOffset.UtcNow,
            GeneratedFacts = Generated(kept)
        };
        var oldDeck = Summary(2, "Generated before tracking") with { LastGeneratedAtUtc = DateTimeOffset.UtcNow };
        var neverGenerated = Summary(3, "Never generated");

        // Then project 1 completes, project 2 gets a new remark and a revised cost.
        var project = await db.Projects.SingleAsync(p => p.Id == 1);
        project.LifecycleStatus = ProjectLifecycleStatus.Completed;
        await db.SaveChangesAsync();
        costs.CostRd[2] = Cost(250m);
        statuses.Latest[2] = new ProjectBriefingExternalStatus(2, 12, "Trials complete", RemarkDate.AddDays(5), DateTime.UtcNow);

        var decks = await service.WithDataMovementAsync(new[] { generatedDeck, oldDeck, neverGenerated });

        var movement = decks[0].DataMovement!;
        Assert.Equal((1, 1, 1), (movement.StageChanges, movement.StatusChanges, movement.CostChanges));
        Assert.Equal(new[] { "Counter-UAS System", "VR Based Map Reading" }, movement.Projects.Select(p => p.ProjectName).OrderBy(n => n));
        Assert.Null(decks[1].DataMovement);
        Assert.Null(decks[2].DataMovement);
    }

    [Fact]
    public async Task List_ReadsBackTheFactsKeptAtGeneration()
    {
        await using var db = CreateDb();
        db.Users.Add(new ApplicationUser { Id = "u", UserName = "u", FullName = "U" });
        db.Set<ProjectBriefingDeck>().Add(new ProjectBriefingDeck
        {
            Id = 5,
            Name = "Deck",
            NormalizedName = "DECK",
            OwnerUserId = "u",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            RowVersion = Guid.NewGuid().ToByteArray()
        });
        await db.SaveChangesAsync();
        var facts = ProjectBriefingSlideFacts.Write(ProjectBriefingLayout.StandardBriefing, ProjectBriefingCostMode.Both, new[] { Fact(1, "DEVP", "a", 1m) });
        var deckService = new ProjectBriefingDeckService(db, Stub<IProjectBriefingSelectionService>(), new NoAudit(), new FixedClock(), NullLogger<ProjectBriefingDeckService>.Instance);

        // ExecuteUpdate is not supported by the in-memory provider; the stored value is what the
        // list reads, so set it as MarkGeneratedAsync does.
        var deck = await db.Set<ProjectBriefingDeck>().SingleAsync();
        deck.LastGeneratedAtUtc = DateTimeOffset.UtcNow;
        deck.LastGeneratedFactsJson = facts;
        await db.SaveChangesAsync();

        var listed = Assert.Single(await deckService.ListAsync("u"));

        Assert.Equal(ProjectBriefingSlideFacts.Read(facts)!.Facts, listed.GeneratedFacts!.Facts);
    }

    // Key points (docs/ux/briefing-decks.md, Plan: Project key points, PR 3).
    private static readonly ProjectBriefingKeyPoint[] PointsBefore =
    {
        new("Training gap", "No live firing in peacetime."),
        new("Op necessity", "Crews must qualify before induction.")
    };

    [Fact]
    public void Compare_CountsEditedKeyPointsOnDecksThatShowedThem()
    {
        var before = Current(1, "DEVP", "x", 1m) with { KeyPoints = PointsBefore };
        var edited = before with
        {
            KeyPoints = new[] { PointsBefore[0], new ProjectBriefingKeyPoint("Op necessity", "Every detachment must qualify.") }
        };
        var shown = All | ProjectBriefingShownFacts.KeyPoints;

        var movement = ProjectBriefingSlideFacts.Compare(
            Generated(before.ToSlideFact(shown)),
            new Dictionary<int, ProjectBriefingCurrentProjectFacts> { [1] = edited });

        Assert.Equal(1, movement.KeyPointChanges);
        Assert.Equal(1, movement.Total);
        Assert.True(Assert.Single(movement.Projects).KeyPoints);
        Assert.Contains("key points edited on 1 project", movement.Describe());
    }

    [Fact]
    public void Compare_IgnoresKeyPointsTheDeckDidNotShowAndFactsKeptBeforeThem()
    {
        var before = Current(1, "DEVP", "x", 1m) with { KeyPoints = PointsBefore };
        var edited = before with { KeyPoints = Array.Empty<ProjectBriefingKeyPoint>() };
        var current = new Dictionary<int, ProjectBriefingCurrentProjectFacts> { [1] = edited };

        // A deck without key points.
        Assert.False(ProjectBriefingSlideFacts.Compare(Generated(before.ToSlideFact(All)), current).HasMoved);

        // Facts written before key points existed have no "k" and read back as not shown.
        var older = ProjectBriefingSlideFacts.Read(
            ProjectBriefingSlideFacts.Write(ProjectBriefingLayout.StandardBriefing, ProjectBriefingCostMode.Both, new[] { before.ToSlideFact(All) }));
        Assert.Null(Assert.Single(older!.Facts).KeyPoints);
        Assert.False(ProjectBriefingSlideFacts.Compare(older, current).HasMoved);
    }

    [Fact]
    public void Compare_AddingTheFirstKeyPointCounts()
    {
        var before = Current(1, "DEVP", "x", 1m);
        var now = before with { KeyPoints = PointsBefore };
        var shown = All | ProjectBriefingShownFacts.KeyPoints;

        var generatedFact = before.ToSlideFact(shown);
        Assert.Equal(string.Empty, generatedFact.KeyPoints);
        Assert.Equal(1, ProjectBriefingSlideFacts.Compare(
            Generated(generatedFact),
            new Dictionary<int, ProjectBriefingCurrentProjectFacts> { [1] = now }).KeyPointChanges);
    }

    [Fact]
    public void Shown_TracksKeyPointsOnlyForProjectsWithSlidesInDecksThatShowThem()
    {
        var project = new ProjectBriefingPresentationProject { ProjectId = 1, ProjectName = "One", KeyPoints = PointsBefore };
        var tableRow = new ProjectBriefingPresentationProject { ProjectId = 2, ProjectName = "Two", TableRowOnly = true };
        var withKeyPoints = DetailedData(
            ProjectBriefingNarrativeMode.ProjectBrief,
            ProjectBriefingStandardSlideOptions.Default with { KeyPoints = ProjectBriefingKeyPointPlacement.ProjectBriefSlide },
            project,
            tableRow);
        var without = DetailedData(
            ProjectBriefingNarrativeMode.ProjectBrief,
            ProjectBriefingStandardSlideOptions.Default,
            project,
            tableRow);

        Assert.True(ProjectBriefingSlideFacts.Shown(project, withKeyPoints).HasFlag(ProjectBriefingShownFacts.KeyPoints));
        Assert.False(ProjectBriefingSlideFacts.Shown(tableRow, withKeyPoints).HasFlag(ProjectBriefingShownFacts.KeyPoints));
        Assert.False(ProjectBriefingSlideFacts.Shown(project, without).HasFlag(ProjectBriefingShownFacts.KeyPoints));
    }

    [Fact]
    public void Shown_KeyPointCapabilitySlideWithAStageAndCostLine_DoesNotDrawTheLifecycleSubtitle()
    {
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 1,
            ProjectName = "One",
            PresentStage = "Development",
            BriefDescription = "- One short capability"
        };
        var options = ProjectBriefingStandardSlideOptions.Default with { KeyPoints = ProjectBriefingKeyPointPlacement.CapabilitySlide };
        var data = DetailedData(ProjectBriefingNarrativeMode.CapabilityOverview, options, project);
        var withoutLine = DetailedData(
            ProjectBriefingNarrativeMode.CapabilityOverview,
            options with { ShowPresentStage = false },
            project);

        Assert.False(ProjectBriefingSlideFacts.Shown(project, data).HasFlag(ProjectBriefingShownFacts.Lifecycle));
        Assert.True(ProjectBriefingSlideFacts.Shown(project, withoutLine).HasFlag(ProjectBriefingShownFacts.Lifecycle));
    }

    private static ProjectBriefingPresentationData DetailedData(
        ProjectBriefingNarrativeMode narrative,
        ProjectBriefingStandardSlideOptions options,
        params ProjectBriefingPresentationProject[] projects)
        => new()
        {
            Layout = ProjectBriefingLayout.StandardBriefing,
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            CostMode = ProjectBriefingCostMode.None,
            NarrativeMode = narrative,
            IncludePortfolioSummarySlide = false,
            StandardSlideOptions = options,
            Projects = projects
        };

    private static ProjectBriefingGeneratedFacts Generated(params ProjectBriefingSlideFact[] facts)
        => new(ProjectBriefingLayout.StandardBriefing, ProjectBriefingCostMode.Both, facts);

    private static ProjectBriefingPresentationData Data(
        ProjectBriefingPresentationMode mode,
        ProjectBriefingCostMode costMode,
        params ProjectBriefingPresentationProject[] projects)
        => new() { Layout = ProjectBriefingLayout.StandardBriefing, PresentationMode = mode, CostMode = costMode, Projects = projects };

    private static ProjectBriefingSlideFact Fact(int id, string stage, string? status, decimal cost)
        => Current(id, stage, status, cost).ToSlideFact(All);

    private static ProjectBriefingCurrentProjectFacts Current(int id, string stage, string? status, decimal cost)
        => new(id, $"Project {id}", ProjectLifecycleStatus.Active, stage, status, status is null ? null : RemarkDate,
            Cost(cost), ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.IPA),
            ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation));

    private static ProjectBriefingCostValue Cost(decimal rupees)
        => new(rupees, ProjectBriefingCostBasis.None, rupees.ToString(), string.Empty);

    private static ProjectBriefingDeckSummaryVm Summary(long id, string name, ProjectBriefingCostMode costMode = ProjectBriefingCostMode.Both)
        => new(id, name, null, default, costMode, 2, DateTimeOffset.UtcNow, null, "U", "U", string.Empty);

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"briefing-data-movement-{Guid.NewGuid()}")
            .Options);

    private static ProjectBriefingDataService Service(ApplicationDbContext db, FakeCosts costs, FakeStatuses statuses)
        => new(
            db,
            costs,
            statuses,
            Stub<IProjectBriefingPhotoLoader>(),
            Stub<IProjectBriefingUpdateSheetFactsResolver>(),
            Stub<IProjectBriefingInstitutionalProfileService>(),
            Stub<ProjectManagement.Services.Ffc.IFfcFootprintService>(),
            Stub<ProjectManagement.Services.Ffc.Presentation.IFfcPresentationMapRenderer>(),
            new FixedClock(),
            NullLogger<ProjectBriefingDataService>.Instance);

    // A stand-in for a dependency these tests never reach.
    private static T Stub<T>() where T : class => DispatchProxy.Create<T, Unused>();

    public class Unused : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new NotSupportedException($"{targetMethod?.Name} is not used by this test.");
    }

    private sealed class FakeCosts : IProjectBriefingCostResolver
    {
        public Dictionary<int, ProjectBriefingCostValue> CostRd { get; } = new();

        public Task<ProjectBriefingResolvedCosts> ResolveCostsAsync(IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default)
            => Task.FromResult(new ProjectBriefingResolvedCosts(
                projectIds.Where(CostRd.ContainsKey).ToDictionary(id => id, id => CostRd[id]),
                new Dictionary<int, ProjectBriefingCostValue>()));

        public async Task<IReadOnlyDictionary<int, ProjectBriefingCostValue>> ResolveCostRdAsync(IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default)
            => (await ResolveCostsAsync(projectIds, cancellationToken)).CostRd;

        public Task<IReadOnlyDictionary<int, ProjectBriefingCostValue>> ResolveProliferationCostAsync(IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<int, ProjectBriefingCostValue>>(new Dictionary<int, ProjectBriefingCostValue>());
    }

    private sealed class FakeStatuses : IProjectBriefingExternalStatusService
    {
        public Dictionary<int, ProjectBriefingExternalStatus> Latest { get; } = new();

        public Task<IReadOnlyDictionary<int, ProjectBriefingExternalStatus>> GetLatestAsync(IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<int, ProjectBriefingExternalStatus>>(
                projectIds.Where(Latest.ContainsKey).ToDictionary(id => id, id => Latest[id]));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null)
            => Task.CompletedTask;
    }
}
