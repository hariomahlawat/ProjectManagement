using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Analytics.Presentation;

// SECTION: CoE slides data (what the two slides show, resolved from the page's own view models)
/// <summary>The two CoE (AR/VR) slides: the roadmap (projects by category), then AR/VR completed projects, proliferation and user training.</summary>
public sealed record CoeSlidesData(
    DateOnly AsOf,
    IReadOnlyList<CoeSlideColumn> Columns,
    int OngoingCount,
    int CompletedCount,
    IReadOnlyList<string> CancelledLeftOut,
    IReadOnlyList<ArVrCompletedGroupVm> Completed,
    CoeSlideTraining? Training,
    CoeSlideProliferation? Proliferation)
{
    public int CompletedTotal => Completed.Sum(group => group.Completed);

    /// <summary>Slide 1's speaker notes: each project's latest cost.</summary>
    public string ProjectsNotes { get; init; } = string.Empty;

    /// <summary>Slide 2's speaker notes: each AR/VR project's units proliferated and proliferation cost.</summary>
    public string SummaryNotes { get; init; } = string.Empty;

    /// <summary>Every completed AR/VR project, in the CoE or outside it: newest completion first, then by name; the tag is the year.</summary>
    public IReadOnlyList<CoeSlideProject> CompletedProjects { get; } = Completed
        .SelectMany(group => group.Projects)
        .OrderByDescending(project => project.Year ?? int.MinValue)
        .ThenBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
        .Select(project => new CoeSlideProject(project.Name, project.Year?.ToString(CultureInfo.InvariantCulture) ?? "—", IsCompleted: true, ProjectId: project.Id))
        .ToList();
}

/// <summary>One category column on slide 1: its projects, ongoing first (furthest stage first), then completed.</summary>
public sealed record CoeSlideColumn(string Name, IReadOnlyList<CoeSlideProject> Projects);

/// <param name="Tag">"Completed", or the current stage's short label ("Devp", "AoN"), as on the stage chart's axis.</param>
/// <param name="Pdc">"PDC 31 Mar 2027" for a project in Development with a PDC set ("· overdue" once it has passed); otherwise null.</param>
/// <param name="PdcOverdue">The Development PDC has passed (before the "as of" day): drawn in red.</param>
/// <param name="ProjectId">The project, for the speaker notes' costs; null in a hand-built row.</param>
public sealed record CoeSlideProject(string Name, string Tag, bool IsCompleted, string? Pdc = null, bool PdcOverdue = false, int? ProjectId = null);

public sealed record CoeSlideTraining(int Courses, int People, int Officers, int Jcos, int OtherRanks);

public sealed record CoeSlideProliferation(int UnitsDelivered, int ReceivingUnits, int Sdd, int Abw515);

/// <summary>
/// Projects the CoE analytics (and, where the caller may see them, the AR/VR training and
/// proliferation figures) into the slide data. No figure is recomputed here: every count comes
/// from <see cref="CoeAnalyticsVm"/> and <see cref="ArVrImpactVm"/>, the same as the page.
/// </summary>
public static class CoeSlidesDataBuilder
{
    public const string CompletedTag = "Completed";

    /// <param name="costs">The costs the speaker notes quote; null leaves the notes out.</param>
    public static CoeSlidesData Build(DateOnly asOf, CoeAnalyticsVm coe, ArVrImpactVm? impact, CoeSlideCostFacts? costs = null)
    {
        ArgumentNullException.ThrowIfNull(coe);

        // Real sub-categories by name, then projects filed straight under a CoE parent.
        var buckets = coe.SubcategoryProjects
            .OrderBy(bucket => bucket.WithoutSubcategory)
            .ThenBy(bucket => bucket.SubcategoryName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var columns = buckets
            .Select(bucket => new CoeSlideColumn(
                bucket.SubcategoryName,
                bucket.Projects
                    .Where(project => project.LifecycleStatus != "Cancelled")
                    .OrderBy(project => project.LifecycleStatus == "Completed")
                    .ThenByDescending(project => project.LifecycleStatus == "Completed" ? 0 : StageRank(project.CurrentStageCode))
                    .ThenBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(project => ToSlideProject(project, asOf))
                    .ToList()))
            .Where(column => column.Projects.Count > 0)
            .ToList();

        var cancelled = buckets
            .SelectMany(bucket => bucket.Projects)
            .Where(project => project.LifecycleStatus == "Cancelled")
            .Select(project => NameOf(project))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        CoeSlideTraining? training = impact is { CanViewTraining: true }
            ? new CoeSlideTraining(
                impact.TrainingCourses,
                impact.TrainingPeople,
                impact.TrainingByYear.Sum(year => year.Officers),
                impact.TrainingByYear.Sum(year => year.Jcos),
                impact.TrainingByYear.Sum(year => year.OtherRanks))
            : null;

        CoeSlideProliferation? proliferation = impact is { CanViewProliferation: true }
            ? new CoeSlideProliferation(
                impact.ProliferationUnitsDelivered,
                impact.ProliferationReceivingUnits,
                impact.ProliferationByYear.Sum(year => year.Sdd),
                impact.ProliferationByYear.Sum(year => year.Abw515))
            : null;

        var data = new CoeSlidesData(
            asOf,
            columns,
            coe.OngoingCount,
            coe.CompletedCount,
            cancelled,
            coe.ArVrCompleted,
            training,
            proliferation);
        if (costs is null)
        {
            return data;
        }

        return data with
        {
            ProjectsNotes = CoeSlideNotesBuilder.ProjectsNotes(columns, costs),
            SummaryNotes = CoeSlideNotesBuilder.SummaryNotes(
                data.CompletedProjects,
                impact is { CanViewProliferation: true } ? impact.ProliferationByProject : null,
                costs)
        };
    }

    /// <summary>Every project the speaker notes quote a cost for: slide 1's projects, the completed AR/VR projects, and any other with proliferation.</summary>
    public static IReadOnlyList<int> NoteProjectIds(CoeAnalyticsVm coe, ArVrImpactVm? impact)
    {
        ArgumentNullException.ThrowIfNull(coe);
        return coe.SubcategoryProjects.SelectMany(bucket => bucket.Projects).Where(project => project.LifecycleStatus != "Cancelled").Select(project => project.Id)
            .Concat(coe.ArVrCompleted.SelectMany(group => group.Projects).Select(project => project.Id))
            .Concat(impact is { CanViewProliferation: true } ? impact.ProliferationByProject.Select(item => item.ProjectId) : Array.Empty<int>())
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// "Completed", or the current stage's short label; a project in Development also carries its PDC
    /// (the current stage's planned due date), marked overdue once it has passed, the project header's rule.
    /// </summary>
    public static CoeSlideProject ToSlideProject(CoeProjectSummaryVm project, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.LifecycleStatus == "Completed")
        {
            return new CoeSlideProject(NameOf(project), CompletedTag, IsCompleted: true, ProjectId: project.Id);
        }

        var tag = string.IsNullOrWhiteSpace(project.CurrentStageCode)
            ? "Ongoing"
            : StageCodes.ShortLabelOf(project.CurrentStageCode.Trim());

        var inDevelopment = string.Equals(project.CurrentStageCode, StageCodes.DEVP, StringComparison.OrdinalIgnoreCase);
        if (!inDevelopment || project.CurrentStagePdc is not { } pdc)
        {
            return new CoeSlideProject(NameOf(project), tag, IsCompleted: false, ProjectId: project.Id);
        }

        var overdue = pdc < asOf;
        var pdcText = $"PDC {pdc.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}{(overdue ? " · overdue" : string.Empty)}";
        return new CoeSlideProject(NameOf(project), tag, IsCompleted: false, pdcText, overdue, project.Id);
    }

    private static string NameOf(CoeProjectSummaryVm project) =>
        string.IsNullOrWhiteSpace(project.Name) ? "Untitled project" : project.Name.Trim();

    // Workflow position: a later stage ranks higher; an unknown or missing stage ranks lowest.
    private static int StageRank(string? stageCode) =>
        string.IsNullOrWhiteSpace(stageCode)
            ? -1
            : Array.FindIndex(StageCodes.All, code => string.Equals(code, stageCode, StringComparison.OrdinalIgnoreCase));
}
// END SECTION
