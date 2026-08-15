using ProjectManagement.Models;
using ProjectManagement.Models.Publications;
using ProjectManagement.Services.Compendiums;
using ProjectManagement.Services.Publications;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase26ContractTests
{
    [Fact]
    public void ReviewFingerprint_ChangesWhenPublicationSectionChanges()
    {
        var input = new CompendiumReviewFingerprintInput(
            42,
            "ASTRAE",
            ProjectLifecycleStatus.Active,
            "Other R&D Projects",
            "AI",
            "Infantry",
            null,
            null,
            null,
            "Capability narrative",
            7,
            CompendiumImageSelectionMode.Explicit,
            .5,
            .5)
        {
            NarrativeSource = CompendiumNarrativeSource.ProjectBrief,
            PublicationSectionKey = "sec-operational",
            PublicationSectionName = "Operational Training"
        };

        var first = CompendiumReviewFingerprint.Create(input);
        var renamed = CompendiumReviewFingerprint.Create(input with
        {
            PublicationSectionName = "Operational Systems"
        });

        Assert.NotEqual(first, renamed);
    }

    [Fact]
    public void SavedCompendium_DefaultsToSchemaFiveAndSupportsIndependentEmptySections()
    {
        var preset = new CompendiumPreset();
        preset.Sections.Add(new CompendiumPresetSection
        {
            SectionKey = "sec-emerging",
            Name = "Emerging Technologies",
            NormalizedName = "EMERGING TECHNOLOGIES",
            SortOrder = 0
        });

        Assert.True(preset.SettingsSchemaVersion >= 5);
        Assert.Single(preset.Sections);
        Assert.Empty(preset.Sections.Single().Projects);
    }

    [Fact]
    public void ProjectNarrativeOverride_IsOptionalAndDoesNotReplacePublicationDefault()
    {
        var configuration = new CompendiumPresetConfiguration(
            "Compendium",
            "Detailed Project Reference",
            "Capability Edition · 2026",
            null,
            new[]
            {
                new CompendiumPresetProjectConfiguration(1)
                {
                    NarrativeSourceOverride = CompendiumNarrativeSource.CapabilityOverview
                },
                new CompendiumPresetProjectConfiguration(2)
            })
        {
            NarrativeSource = CompendiumNarrativeSource.ProjectBrief
        };

        Assert.Equal(CompendiumNarrativeSource.ProjectBrief, configuration.NarrativeSource);
        Assert.Equal(CompendiumNarrativeSource.CapabilityOverview, configuration.Projects[0].NarrativeSourceOverride);
        Assert.Null(configuration.Projects[1].NarrativeSourceOverride);
    }

    [Fact]
    public void MissingSponsoringLineDirectorate_IsInformationalRatherThanPublicationWarning()
    {
        var assessment = new CompendiumReadinessPolicy().Evaluate(new CompendiumProjectReadinessContext(
            1,
            "Example",
            ProjectLifecycleStatus.Active,
            null,
            null,
            "Project brief",
            null,
            null,
            10,
            true,
            CompendiumImageSelectionMode.Automatic,
            200,
            false,
            "current",
            "current"));

        var lineDirectorate = Assert.Single(assessment.Findings.Where(finding => finding.Code == "missingSponsoringLineDirectorate"));
        Assert.Equal(CompendiumFindingSeverity.Information, lineDirectorate.Severity);
    }
}
