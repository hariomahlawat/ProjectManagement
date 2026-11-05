using System.Collections.Generic;
using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings;

/// <summary>
/// The deck settings a template replaced, so "Undo" can put them back. Undo is allowed only while
/// the deck is still at <see cref="AppliedRowVersion"/>, the version the template left it at: any
/// later change (by anyone) ends it.
/// </summary>
public sealed record ProjectBriefingPresetUndo(
    long DeckId,
    string AppliedRowVersion,
    ProjectBriefingDeckPreset? Preset,
    ProjectBriefingLayout Layout,
    ProjectBriefingPresentationMode PresentationMode,
    ProjectBriefingNarrativeMode NarrativeMode,
    ProjectBriefingCostMode CostMode,
    ProjectBriefingPresentationTheme PresentationTheme,
    bool IncludeStageSummary,
    bool IncludeCoverSlide,
    bool IncludePortfolioSummarySlide,
    IReadOnlyList<ProjectBriefingUpdateSheetRow> UpdateSheetRows,
    bool HideEmptyUpdateSheetValues,
    bool ShowPresentStage,
    bool ShowPresentStatus,
    ProjectBriefingClosingSlideType ClosingSlideType,
    bool ProfileIncluded,
    bool ProfileIncludesHistory,
    IReadOnlyList<ProjectBriefingInstitutionalProfileModule> ProfileModules,
    IReadOnlyList<ProjectBriefingAdditionalSlideType> AdditionalSlideOrder);

/// <summary>What applying a template did: whether it switched on an additional slide, and how to undo it.</summary>
public sealed record ProjectBriefingPresetApplied(bool AdditionalSlidesChanged, ProjectBriefingPresetUndo Undo);
