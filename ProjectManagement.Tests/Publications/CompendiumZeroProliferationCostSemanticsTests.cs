using System.Linq;
using ProjectManagement.Models;
using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumZeroProliferationCostSemanticsTests
{
    [Fact]
    public void Evaluate_ExplicitZeroCost_IsRecordedInformationNotMissingCostWarning()
    {
        var policy = new CompendiumReadinessPolicy();
        var assessment = policy.Evaluate(new CompendiumProjectReadinessContext(
            ProjectId: 1,
            ProjectName: "ZERO COST PROJECT",
            LifecycleStatus: ProjectLifecycleStatus.Active,
            CompletionYear: null,
            SponsoringLineDirectorate: "All Arms / Services",
            Description: "A valid project narrative for publication readiness testing.",
            ProliferationCostLakhs: 0m,
            ProliferationAvailability: true,
            ResolvedPhotoId: null,
            ResolvedPhotoUsable: false,
            ImageSelectionMode: CompendiumImageSelectionMode.Automatic,
            EffectiveDpi: null,
            ExplicitPhotoUnavailable: true,
            CurrentReviewFingerprint: "fingerprint",
            SubmittedReviewFingerprint: "fingerprint"));

        Assert.DoesNotContain(CompendiumPublicationIssue.MissingProliferationCost, assessment.PublicationIssues);
        Assert.Contains(CompendiumPublicationIssue.ZeroProliferationCost, assessment.PublicationIssues);

        var zeroCostFinding = Assert.Single(assessment.Findings.Where(finding => finding.Code == "zeroCost"));
        Assert.Equal(CompendiumFindingSeverity.Information, zeroCostFinding.Severity);
        Assert.Contains("explicitly recorded as zero", zeroCostFinding.Message);
    }
}
