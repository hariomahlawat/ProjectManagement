using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings;

// One rule for "this project is missing content the deck will show". The Studio's outline,
// slide checks and Checks tab all read these keys, so they always agree with each other.
public static class ProjectBriefingContentGaps
{
    public const string Status = "status";
    public const string CostRd = "costRd";
    public const string ProliferationCost = "proliferationCost";
    public const string Photo = "photo";
    public const string CapabilityOverview = "capabilityOverview";
    public const string ProjectBrief = "projectBrief";
    public const string UpdateSheetFacts = "updateSheetFacts";

    public static IReadOnlyList<string> AllKeys { get; } = new[]
    {
        Status, CostRd, ProliferationCost, Photo, CapabilityOverview, ProjectBrief, UpdateSheetFacts
    };

    public static IReadOnlyDictionary<int, IReadOnlyList<string>> Resolve(
        IEnumerable<ProjectBriefingProjectVm> projects,
        ProjectBriefingContentGapSettings settings)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(settings);
        var uses = Uses.From(settings);
        var tableRowUses = Uses.TableRow(settings);
        return projects
            .Where(project => project.IsPresented)
            .ToDictionary(
                project => project.ProjectId,
                project => For(project, project.Presentation == ProjectBriefingItemPresentation.TableRowOnly ? tableRowUses : uses));
    }

    private static IReadOnlyList<string> For(ProjectBriefingProjectVm project, Uses uses)
    {
        var gaps = new List<string>(4);
        if (uses.Status && string.IsNullOrWhiteSpace(project.ExternalStatus)) gaps.Add(Status);
        if (uses.CostRd && !project.CostRd.IsAvailable) gaps.Add(CostRd);
        if (uses.ProliferationCost && !project.ProliferationCost.IsAvailable) gaps.Add(ProliferationCost);
        if (uses.Photo && !project.HasCoverPhoto) gaps.Add(Photo);
        if (uses.CapabilityOverview && !HasCapabilityOverview(project.BriefDescription)) gaps.Add(CapabilityOverview);
        if (uses.ProjectBrief && !HasProjectBrief(project.ProjectBrief)) gaps.Add(ProjectBrief);
        if (uses.UpdateSheetFacts && !project.IsUpdateSheetCoreFactsReady) gaps.Add(UpdateSheetFacts);
        return gaps;
    }

    public static bool HasCapabilityOverview(string? value)
    {
        var text = value?.Trim();
        return !string.IsNullOrWhiteSpace(text)
            && !string.Equals(text, "Brief description not recorded.", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "Capability overview not recorded.", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasProjectBrief(string? value)
    {
        var text = value?.Trim();
        return !string.IsNullOrWhiteSpace(text)
            && !string.Equals(text, "Project brief not recorded.", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record Uses(
        bool Status,
        bool CostRd,
        bool ProliferationCost,
        bool Photo,
        bool CapabilityOverview,
        bool ProjectBrief,
        bool UpdateSheetFacts)
    {
        // A table row shows the present stage, the status remark and the deck's costs.
        public static Uses TableRow(ProjectBriefingContentGapSettings settings) => new(
            Status: true,
            CostRd: settings.CostMode is ProjectBriefingCostMode.CostRdOnly or ProjectBriefingCostMode.Both,
            ProliferationCost: settings.CostMode is ProjectBriefingCostMode.ProliferationOnly or ProjectBriefingCostMode.Both,
            Photo: false,
            CapabilityOverview: false,
            ProjectBrief: false,
            UpdateSheetFacts: false);

        // Mirrors which content each template, format and narrative choice puts on a slide.
        public static Uses From(ProjectBriefingContentGapSettings settings)
        {
            if (settings.Layout == ProjectBriefingLayout.ProjectUpdateSheet)
            {
                var rows = settings.UpdateSheetOptions.Rows;
                var showsEmptyRows = !settings.UpdateSheetOptions.HideEmptyValues;
                return new Uses(
                    Status: showsEmptyRows && rows.Contains(ProjectBriefingUpdateSheetRow.PresentStatus),
                    CostRd: showsEmptyRows && rows.Contains(ProjectBriefingUpdateSheetRow.ProjectCost),
                    ProliferationCost: false,
                    Photo: true,
                    CapabilityOverview: false,
                    ProjectBrief: true,
                    UpdateSheetFacts: true);
            }

            var executiveTable = settings.PresentationMode
                is ProjectBriefingPresentationMode.ExecutiveTable or ProjectBriefingPresentationMode.Combined;
            var detailed = settings.PresentationMode
                is ProjectBriefingPresentationMode.DetailedProjects or ProjectBriefingPresentationMode.Combined;
            return new Uses(
                Status: executiveTable || (detailed && settings.StandardSlideOptions.ShowPresentStatus),
                CostRd: settings.CostMode is ProjectBriefingCostMode.CostRdOnly or ProjectBriefingCostMode.Both,
                ProliferationCost: settings.CostMode is ProjectBriefingCostMode.ProliferationOnly or ProjectBriefingCostMode.Both,
                Photo: detailed,
                CapabilityOverview: detailed && settings.NarrativeMode
                    is ProjectBriefingNarrativeMode.CapabilityOverview or ProjectBriefingNarrativeMode.Both,
                ProjectBrief: detailed && settings.NarrativeMode
                    is ProjectBriefingNarrativeMode.ProjectBrief or ProjectBriefingNarrativeMode.Both,
                UpdateSheetFacts: false);
        }
    }
}

public sealed record ProjectBriefingContentGapSettings(
    ProjectBriefingLayout Layout,
    ProjectBriefingPresentationMode PresentationMode,
    ProjectBriefingCostMode CostMode,
    ProjectBriefingNarrativeMode NarrativeMode,
    ProjectBriefingStandardSlideOptions StandardSlideOptions,
    ProjectBriefingUpdateSheetOptions UpdateSheetOptions);
