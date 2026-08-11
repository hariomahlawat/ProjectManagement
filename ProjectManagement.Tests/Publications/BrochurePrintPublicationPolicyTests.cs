using ProjectManagement.Services.Publications;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class BrochurePrintPublicationPolicyTests
{
    [Fact]
    public void Validate_ApprovedReference_HasNoPrintMatterBlockers()
    {
        var issues = BrochurePrintPublicationPolicy.Validate(
            BrochurePublicationProfile.PrintCompact,
            BrochurePrintPublicationPolicy.ApprovedReference);

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_MissingSection_IsAuthoritativePreflightBlocker()
    {
        var approved = BrochurePrintPublicationPolicy.ApprovedReference;
        var matter = approved with { ProcurementGuidance = null };

        var issues = BrochurePrintPublicationPolicy.Validate(
            BrochurePublicationProfile.PrintCompact,
            matter);

        var issue = Assert.Single(issues);
        Assert.Equal(BrochurePreflightIssueCode.PrintInstitutionalContentMissing, issue.Code);
        Assert.Equal(PublicationIssueSeverity.Blocker, issue.Severity);
        Assert.Contains("Procurement", issue.Message);
    }

    [Fact]
    public void Validate_OverlongSection_IsAuthoritativePreflightBlocker()
    {
        var approved = BrochurePrintPublicationPolicy.ApprovedReference;
        var longText = string.Join(" ", Enumerable.Repeat("word", BrochurePrintPublicationPolicy.NewSimulatorsMaximumWords + 1));
        var matter = approved with { NewSimulatorsGuidance = longText };

        var issues = BrochurePrintPublicationPolicy.Validate(
            BrochurePublicationProfile.PrintCompact,
            matter);

        var issue = Assert.Single(issues);
        Assert.Equal(BrochurePreflightIssueCode.PrintInstitutionalContentTooLong, issue.Code);
        Assert.Equal(PublicationIssueSeverity.Blocker, issue.Severity);
    }

    [Fact]
    public void Validate_DigitalProfile_DoesNotApplyHardCopyMatterRules()
    {
        var issues = BrochurePrintPublicationPolicy.Validate(
            BrochurePublicationProfile.DigitalComfortable,
            matter: null);

        Assert.Empty(issues);
    }
}
