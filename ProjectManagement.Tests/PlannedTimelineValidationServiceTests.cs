using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Scheduling;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class PlannedTimelineValidationServiceTests
{
    [Fact]
    public async Task ValidateAsync_RejectsAnIntrinsicReversedRangeButAllowsPartialDates()
    {
        await using var fixture = await Fixture.CreateAsync();
        var candidate = fixture.Candidate(stages:
        [
            new(StageCodes.FS, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 19)),
            new(StageCodes.IPA, new DateOnly(2026, 10, 21), null)
        ]);

        var result = await fixture.Service.ValidateAsync(candidate);

        Assert.False(result.IsValid);
        Assert.Equal(new[] { "Feasibility Study Planned due must be on or after its Planned start." }, result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_RejectsMissingConfiguredStageButAllowsItsDatesToRemainPartial()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            Code = StageCodes.IPA,
            Name = "In-Principle Approval",
            Sequence = 20
        });
        await fixture.Db.SaveChangesAsync();
        var fs = new PlannedTimelineStage(
            StageCodes.FS,
            new DateOnly(2026, 10, 20),
            new DateOnly(2026, 10, 21));

        var missing = await fixture.Service.ValidateAsync(fixture.Candidate(stages: [fs]));
        var partial = await fixture.Service.ValidateAsync(fixture.Candidate(stages:
        [
            fs,
            new(StageCodes.IPA, null, null)
        ]));

        Assert.Equal(
            new[] { "Stage In-Principle Approval (IPA) is missing from the plan." },
            missing.Errors);
        Assert.True(partial.IsValid);
    }

    [Theory]
    [InlineData(PlanTransitionRule.SameDay, "2026-10-23", true)]
    [InlineData(PlanTransitionRule.NextWorkingDay, "2026-10-23", false)]
    [InlineData(PlanTransitionRule.NextWorkingDay, "2026-10-26", true)]
    public async Task ValidateAsync_AppliesSameDayAndNextWorkingDayLiterally(
        PlanTransitionRule rule,
        string successorStart,
        bool expectedValid)
    {
        await using var fixture = await Fixture.CreateAsync();
        var candidate = fixture.Candidate(
            rule: rule,
            stages:
            [
                new(StageCodes.FS, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 23)),
                new(StageCodes.IPA, DateOnly.Parse(successorStart), new DateOnly(2026, 10, 30))
            ]);

        var result = await fixture.Service.ValidateAsync(candidate);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData(false, true, null, false, "2026-10-26")]
    [InlineData(true, true, null, false, "2026-10-24")]
    [InlineData(false, true, HolidayType.Gazetted, true, "2026-10-27")]
    [InlineData(false, true, HolidayType.Restricted, true, "2026-10-27")]
    [InlineData(false, true, HolidayType.Restricted, false, "2026-10-26")]
    [InlineData(false, false, HolidayType.Gazetted, true, "2026-10-26")]
    public async Task ValidateAsync_UsesTheCandidateWeekendPolicyAndAuthoritativeOfficeHolidayRules(
        bool includeWeekends,
        bool skipHolidays,
        HolidayType? holidayType,
        bool observed,
        string expectedMinimum)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (holidayType.HasValue)
        {
            fixture.Db.Holidays.Add(new Holiday
            {
                Date = new DateOnly(2026, 10, 26),
                Name = "Calendar test",
                Type = holidayType.Value,
                IsObservedAsOfficeHoliday = observed
            });
            await fixture.Db.SaveChangesAsync();
        }

        var expected = DateOnly.Parse(expectedMinimum);
        var candidate = fixture.Candidate(
            includeWeekends: includeWeekends,
            skipHolidays: skipHolidays,
            stages:
            [
                new(StageCodes.FS, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 23)),
                new(StageCodes.IPA, expected, expected)
            ]);

        var valid = await fixture.Service.ValidateAsync(candidate);
        var invalid = await fixture.Service.ValidateAsync(candidate with
        {
            Stages =
            [
                candidate.Stages[0],
                new PlannedTimelineStage(StageCodes.IPA, expected.AddDays(-1), expected)
            ]
        });

        Assert.True(valid.IsValid);
        Assert.False(invalid.IsValid);
        Assert.Contains(expected.ToString("dd MMM yyyy"), invalid.Errors.Single());
    }

    [Fact]
    public async Task ValidateAsync_ChecksEveryDirectPredecessor()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.ValidateAsync(fixture.Candidate(
            rule: PlanTransitionRule.SameDay,
            stages:
            [
                new(StageCodes.TEC, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5)),
                new(StageCodes.BM, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8)),
                new(StageCodes.COB, new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 9))
            ]));

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("Benchmarking", result.Errors[0]);
    }

    // "Commercial Bid Opening" is the official name; a stored template that still says
    // "Commercial Opening" must not leak into messages the timeline would never show.
    [Fact]
    public async Task ValidateAsync_NamesStagesByTheirOfficialWorkflowName()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            Code = StageCodes.COB,
            Name = "Commercial Opening",
            Sequence = 70
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.ValidateAsync(fixture.Candidate(
            rule: PlanTransitionRule.SameDay,
            stages:
            [
                new(StageCodes.TEC, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5)),
                new(StageCodes.BM, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8)),
                new(StageCodes.COB, new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 9))
            ]));

        var error = Assert.Single(result.Errors);
        Assert.StartsWith("Commercial Bid Opening Planned start", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Commercial Opening ", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PlanConstants.StageTemplateVersionV1, "SOW Vetting", "In-Principle Approval")]
    [InlineData(PlanConstants.StageTemplateVersionV2, "In-Principle Approval", "SOW Vetting")]
    public async Task ValidateAsync_UsesTheSelectedWorkflowVersion(
        string workflowVersion,
        string expectedStage,
        string expectedPredecessor)
    {
        await using var fixture = await Fixture.CreateAsync(workflowVersion);
        var result = await fixture.Service.ValidateAsync(fixture.Candidate(
            rule: PlanTransitionRule.SameDay,
            stages:
            [
                new(StageCodes.FS, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)),
                new(StageCodes.SOW, new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 6)),
                new(StageCodes.IPA, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 7))
            ]));

        Assert.False(result.IsValid);
        Assert.Contains(expectedStage, result.Errors.Single());
        Assert.Contains(expectedPredecessor, result.Errors.Single());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ValidateAsync_UsesCandidatePncInsteadOfPublishedPnc(
        bool candidatePnc,
        bool expectedValid)
    {
        await using var fixture = await Fixture.CreateAsync(publishedPnc: !candidatePnc);
        var result = await fixture.Service.ValidateAsync(fixture.Candidate(
            pncApplicable: candidatePnc,
            rule: PlanTransitionRule.SameDay,
            stages:
            [
                new(StageCodes.COB, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)),
                new(StageCodes.PNC, new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 10)),
                new(StageCodes.EAS, new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 12))
            ]));

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_SkippedStagesNeitherFailIntrinsicValidationNorConstrainSuccessors()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.ValidateAsync(fixture.Candidate(
            rule: PlanTransitionRule.SameDay,
            stages:
            [
                new(StageCodes.FS, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 10), IsSkipped: true),
                new(StageCodes.IPA, new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 12))
            ]));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_DoesNotApplyNewValidationBeforeTheEffectiveAnchor()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.ValidateAsync(fixture.Candidate(
            anchorStageCode: StageCodes.IPA,
            stages:
            [
                new(StageCodes.FS, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 10)),
                new(StageCodes.IPA, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2))
            ]));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_UsesConfiguredStageSequenceForCustomAnchorScope()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.StageTemplates.AddRange(
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersionV1,
                Code = StageCodes.FS,
                Name = "Feasibility Study",
                Sequence = 10
            },
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersionV1,
                Code = StageCodes.IPA,
                Name = "In-Principle Approval",
                Sequence = 20
            },
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersionV1,
                Code = "CUSTOM",
                Name = "Custom Review",
                Sequence = 25
            },
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersionV1,
                Code = StageCodes.SOW,
                Name = "SOW Vetting",
                Sequence = 30
            });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.ValidateAsync(fixture.Candidate(
            anchorStageCode: "CUSTOM",
            stages:
            [
                new(StageCodes.FS, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 10)),
                new("CUSTOM", new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 22)),
                new(StageCodes.SOW, new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 24))
            ]));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public async Task ValidateAsync_ValidatesExtensionIntrinsicDatesAndConfiguredDependenciesWithoutUsingSortOrder()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            Code = "EXT",
            Name = "Extension Review",
            Sequence = 900
        });
        fixture.Db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            FromStageCode = "EXT",
            DependsOnStageCode = StageCodes.FS
        });
        await fixture.Db.SaveChangesAsync();

        var dependencyFailure = await fixture.Service.ValidateAsync(fixture.Candidate(
            rule: PlanTransitionRule.SameDay,
            stages:
            [
                new(StageCodes.FS, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8), SortOrder: 999),
                new("EXT", new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 9), SortOrder: -100)
            ]));
        var intrinsicFailure = await fixture.Service.ValidateAsync(fixture.Candidate(
            stages: [new("EXT", new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 8))]));

        Assert.Contains(dependencyFailure.Errors, error => error.Contains("Extension Review") && error.Contains("Feasibility Study"));
        Assert.Contains(intrinsicFailure.Errors, error => error.Contains("Extension Review") && error.Contains("Planned due"));
    }

    [Fact]
    public async Task ValidateAsync_UsesTheFinalCaseInsensitiveCandidateRow()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.ValidateAsync(fixture.Candidate(
            rule: PlanTransitionRule.SameDay,
            stages:
            [
                new(StageCodes.FS, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8)),
                new(StageCodes.IPA, new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 9)),
                new(StageCodes.IPA.ToLowerInvariant(), new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 9))
            ]));

        Assert.True(result.IsValid);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(ApplicationDbContext db)
        {
            Db = db;
            var workflow = new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider());
            Service = new PlannedTimelineValidationService(db, workflow, new OfficeCalendarService(db));
        }

        public ApplicationDbContext Db { get; }
        public IPlannedTimelineValidationService Service { get; }

        public static async Task<Fixture> CreateAsync(
            string workflowVersion = PlanConstants.StageTemplateVersionV1,
            bool publishedPnc = true)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new ApplicationDbContext(options);
            db.Projects.Add(new Project
            {
                Id = 1,
                Name = "Canonical validation test",
                LeadPoUserId = "po",
                WorkflowVersion = workflowVersion,
                PncApplicable = publishedPnc
            });
            await db.SaveChangesAsync();
            return new Fixture(db);
        }

        public PlannedTimelineCandidate Candidate(
            PlannedTimelineStage[] stages,
            string? anchorStageCode = StageCodes.FS,
            PlanTransitionRule rule = PlanTransitionRule.NextWorkingDay,
            bool includeWeekends = false,
            bool skipHolidays = true,
            bool pncApplicable = true) =>
            new(1, anchorStageCode, rule, includeWeekends, skipHolidays, pncApplicable, stages);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}