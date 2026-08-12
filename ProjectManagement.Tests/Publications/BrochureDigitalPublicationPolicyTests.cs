using ProjectManagement.Services.Publications;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class BrochureDigitalPublicationPolicyTests
{
    [Fact]
    public void Plan_WithInstitutionalMatterAndBackCover_ProducesCompleteEditorialSequence()
    {
        var projects = Enumerable.Range(1, 4)
            .Select(id => Project(id, 150))
            .ToArray();
        var matter = new BrochurePrintMatter(
            "Centre statement",
            "Opening narrative",
            "Future narrative",
            "Procurement guidance",
            "Developing agency",
            "Manufacturing agency",
            "Visionary horizons",
            "New simulator guidance");

        var plan = BrochureDigitalPublicationPolicy.Plan(
            projects,
            matter,
            additionalIntroduction: null,
            includeBackCover: true);

        Assert.True(plan.IncludesInstitutionalOpening);
        Assert.True(plan.IncludesInstitutionalClosing);
        Assert.True(plan.IncludesBackCover);
        Assert.Equal(2, plan.ProjectPageCount);
        Assert.Equal(6, plan.EstimatedTotalPageCount); // cover + opening + 2 project + closing + back
        Assert.Equal(new[] { "cover", "institutional-opening", "projects", "projects", "institutional-closing", "back-cover" },
            plan.PagePlan.Select(page => page.Kind));
    }

    [Fact]
    public void Plan_AdditionalIntroduction_IsPaginatedWithoutReducingProjectDensityFloor()
    {
        var introduction = string.Join(" ", Enumerable.Range(1, 650).Select(index => $"intro{index}"));
        var plan = BrochureDigitalPublicationPolicy.Plan(
            new[] { Project(1, 120) },
            institutionalMatter: null,
            additionalIntroduction: introduction,
            includeBackCover: false);

        Assert.Equal(2, plan.AdditionalIntroductionPages.Count);
        Assert.All(plan.AdditionalIntroductionPages,
            page => Assert.InRange(BrochureLayoutPlanner.CountWords(page), 1, 330));
        Assert.Equal(4, plan.EstimatedTotalPageCount); // cover + 2 intro + project
    }

    [Fact]
    public void ValidateInstitutionalMatter_BlocksOnlyWhenDigitalEditorialPagesAreOverloaded()
    {
        var longOpening = string.Join(" ", Enumerable.Repeat("word", 431));
        var longClosing = string.Join(" ", Enumerable.Repeat("word", 421));
        var matter = new BrochurePrintMatter(
            null,
            longOpening,
            null,
            null,
            null,
            null,
            longClosing,
            null);

        var issues = BrochureDigitalPublicationPolicy.ValidateInstitutionalMatter(matter);

        Assert.Contains(issues, issue => issue.Code == BrochurePreflightIssueCode.DigitalInstitutionalOpeningTooLong
                                         && issue.Severity == PublicationIssueSeverity.Blocker);
        Assert.Contains(issues, issue => issue.Code == BrochurePreflightIssueCode.DigitalInstitutionalClosingTooLong
                                         && issue.Severity == PublicationIssueSeverity.Blocker);
    }

    private static BrochurePublicationProject Project(int id, int wordCount)
    {
        var narrative = string.Join(" ", Enumerable.Range(1, wordCount).Select(index => $"w{index}"));
        return new BrochurePublicationProject(
            id,
            $"Digital Project {id}",
            "Other R&D Projects",
            "AR / VR",
            narrative,
            wordCount,
            PrimaryPhoto: null,
            SecondaryPhoto: null,
            ImageMode: BrochureImageMode.Automatic);
    }
}
