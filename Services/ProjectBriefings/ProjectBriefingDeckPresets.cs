using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings;

public enum ProjectBriefingDeckPreset
{
    MonthlyCommandReview = 1,
    VisitOrExternalAudience = 2,
    ProjectUpdateSheets = 3
}

// The deck settings a preset decides. Name, description, marking, insignia and the deck's own
// content (projects, overrides, additional-slide text) are never touched by a preset.
public sealed record ProjectBriefingDeckPresetState(
    ProjectBriefingLayout Layout,
    ProjectBriefingPresentationMode PresentationMode,
    ProjectBriefingNarrativeMode NarrativeMode,
    ProjectBriefingCostMode CostMode,
    bool ShowPresentStage,
    bool ShowPresentStatus,
    bool IncludeStageSummary,
    ProjectBriefingPresentationTheme PresentationTheme,
    ProjectBriefingClosingSlideType ClosingSlideType,
    IReadOnlyList<ProjectBriefingUpdateSheetRow> UpdateSheetRows,
    bool HideEmptyUpdateSheetValues,
    bool IncludeCoverSlide,
    bool IncludePortfolioSummarySlide,
    bool IncludesInstitutionalProfile);

public sealed record ProjectBriefingDeckPresetDefinition(
    ProjectBriefingDeckPreset Preset,
    string Title,
    string Summary,
    ProjectBriefingDeckPresetState State);

public static class ProjectBriefingDeckPresets
{
    public static IReadOnlyList<ProjectBriefingDeckPresetDefinition> All { get; } = new[]
    {
        new ProjectBriefingDeckPresetDefinition(
            ProjectBriefingDeckPreset.MonthlyCommandReview,
            "Monthly command review",
            "Status table, stage summary and a capability slide per project, with both costs.",
            new ProjectBriefingDeckPresetState(
                ProjectBriefingLayout.StandardBriefing,
                ProjectBriefingPresentationMode.Combined,
                ProjectBriefingNarrativeMode.CapabilityOverview,
                ProjectBriefingCostMode.Both,
                ShowPresentStage: true,
                ShowPresentStatus: true,
                IncludeStageSummary: true,
                ProjectBriefingPresentationTheme.EditorialLight,
                ProjectBriefingClosingSlideType.JaiHind,
                ProjectBriefingUpdateSheetOptions.RecommendedRows,
                HideEmptyUpdateSheetValues: false,
                IncludeCoverSlide: true,
                IncludePortfolioSummarySlide: true,
                IncludesInstitutionalProfile: false)),
        new ProjectBriefingDeckPresetDefinition(
            ProjectBriefingDeckPreset.VisitOrExternalAudience,
            "Visit or external audience",
            "SDD profile, then a capability slide with photograph per project; no costs or status remarks; Thank You.",
            new ProjectBriefingDeckPresetState(
                ProjectBriefingLayout.StandardBriefing,
                ProjectBriefingPresentationMode.DetailedProjects,
                ProjectBriefingNarrativeMode.CapabilityOverview,
                ProjectBriefingCostMode.None,
                ShowPresentStage: true,
                ShowPresentStatus: false,
                IncludeStageSummary: true,
                ProjectBriefingPresentationTheme.EditorialLight,
                ProjectBriefingClosingSlideType.ThankYou,
                ProjectBriefingUpdateSheetOptions.RecommendedRows,
                HideEmptyUpdateSheetValues: false,
                IncludeCoverSlide: true,
                IncludePortfolioSummarySlide: true,
                IncludesInstitutionalProfile: true)),
        new ProjectBriefingDeckPresetDefinition(
            ProjectBriefingDeckPreset.ProjectUpdateSheets,
            "Project update sheets",
            "One formal sheet per project with the recommended facts, brief and photograph.",
            new ProjectBriefingDeckPresetState(
                ProjectBriefingLayout.ProjectUpdateSheet,
                ProjectBriefingPresentationMode.Combined,
                ProjectBriefingNarrativeMode.ProjectBrief,
                ProjectBriefingCostMode.CostRdOnly,
                ShowPresentStage: true,
                ShowPresentStatus: true,
                IncludeStageSummary: false,
                ProjectBriefingPresentationTheme.EditorialLight,
                ProjectBriefingClosingSlideType.JaiHind,
                ProjectBriefingUpdateSheetOptions.RecommendedRows,
                HideEmptyUpdateSheetValues: false,
                IncludeCoverSlide: true,
                IncludePortfolioSummarySlide: true,
                IncludesInstitutionalProfile: false))
    };

    public static ProjectBriefingDeckPresetDefinition Get(ProjectBriefingDeckPreset preset)
        => All.FirstOrDefault(definition => definition.Preset == preset)
            ?? throw new InvalidOperationException("The selected preset is not available.");

    public static bool IsDefined(ProjectBriefingDeckPreset preset) => All.Any(definition => definition.Preset == preset);

    // Whether the deck still has every setting its preset decides. Only the settings that matter
    // for the chosen template are compared: an update-sheet deck ignores standard-briefing
    // choices, and a standard deck ignores the update-sheet rows. The SDD profile counts only
    // for a preset that adds it; the others leave it as the deck had it.
    public static bool Matches(ProjectBriefingDeckPreset preset, ProjectBriefingDeckPresetState current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var expected = Get(preset).State;
        if (current.Layout != expected.Layout
            || current.PresentationTheme != expected.PresentationTheme
            || current.ClosingSlideType != expected.ClosingSlideType
            || (expected.IncludesInstitutionalProfile && !current.IncludesInstitutionalProfile))
        {
            return false;
        }

        if (expected.Layout == ProjectBriefingLayout.ProjectUpdateSheet)
        {
            return current.UpdateSheetRows.SequenceEqual(expected.UpdateSheetRows)
                && current.HideEmptyUpdateSheetValues == expected.HideEmptyUpdateSheetValues
                && current.IncludeCoverSlide == expected.IncludeCoverSlide
                && current.IncludePortfolioSummarySlide == expected.IncludePortfolioSummarySlide;
        }

        return current.PresentationMode == expected.PresentationMode
            && current.NarrativeMode == expected.NarrativeMode
            && current.CostMode == expected.CostMode
            && current.ShowPresentStage == expected.ShowPresentStage
            && current.ShowPresentStatus == expected.ShowPresentStatus
            && current.IncludeStageSummary == expected.IncludeStageSummary;
    }
}
