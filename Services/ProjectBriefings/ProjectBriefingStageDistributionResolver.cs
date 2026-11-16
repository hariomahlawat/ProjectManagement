using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.ProjectBriefings;

/// <summary>
/// Places each presented project of a deck in a progress band for the stage-wise distribution slides, with the
/// same rule as Analytics › ARPP (<see cref="ProjectProgressBands"/>): the AoN and supply-order dates and the
/// Development PDC from <see cref="IProjectFormalUpdateFactsResolver"/>, the AoN stage statuses, the parent
/// category through the category hierarchy, and the completion month. The stage is the deck's own present
/// stage, so the slides agree with the rest of the deck.
/// </summary>
internal sealed class ProjectBriefingStageDistributionResolver
{
    internal const string NotCategorised = "Not categorised";

    private readonly ApplicationDbContext _db;
    private readonly IProjectFormalUpdateFactsResolver _formalFacts;

    public ProjectBriefingStageDistributionResolver(ApplicationDbContext db, IProjectFormalUpdateFactsResolver? formalFacts = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _formalFacts = formalFacts ?? new ProjectFormalUpdateFactsResolver(db);
    }

    public async Task<ProjectBriefingStageDistributionData> ResolveAsync(
        IReadOnlyList<ProjectBriefingPresentationProject> projects,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var ids = projects.Select(project => project.ProjectId).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new ProjectBriefingStageDistributionData { AsOf = asOf };
        }

        var formalFacts = await _formalFacts.ResolveAsync(ids, cancellationToken);
        var aonStatuses = (await _db.ProjectStages
                .AsNoTracking()
                .Where(stage => ids.Contains(stage.ProjectId) && stage.StageCode == StageCodes.AON)
                .Select(stage => new { stage.ProjectId, stage.Status })
                .ToListAsync(cancellationToken))
            .GroupBy(stage => stage.ProjectId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<StageStatus>)group.Select(stage => stage.Status).ToList());
        var records = (await _db.Projects
                .AsNoTracking()
                .Where(project => ids.Contains(project.Id))
                .Select(project => new { project.Id, project.CategoryId, project.CompletedOn, project.CompletedYear, project.CompletedMonth })
                .ToListAsync(cancellationToken))
            .ToDictionary(project => project.Id);
        var categories = await _db.ProjectCategories
            .AsNoTracking()
            .Select(category => new { category.Id, category.Name, category.ParentId, category.SortOrder })
            .ToListAsync(cancellationToken);
        var hierarchy = categories.ToDictionary(
            category => category.Id,
            category => new ProjectCategoryHierarchyNode(category.Id, category.Name, category.ParentId));
        var rootOrder = categories
            .Where(category => category.ParentId == null)
            .GroupBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Min(category => category.SortOrder), StringComparer.OrdinalIgnoreCase);

        var facts = projects.ToDictionary(
            project => project.ProjectId,
            project =>
            {
                records.TryGetValue(project.ProjectId, out var record);
                formalFacts.TryGetValue(project.ProjectId, out var formal);
                return new ProjectStandingFacts(
                    ProjectCategoryHierarchyResolver.ResolveRoot(record?.CategoryId, hierarchy)?.Name,
                    formal?.AonDate,
                    aonStatuses.GetValueOrDefault(project.ProjectId) ?? Array.Empty<StageStatus>(),
                    formal?.SupplyOrderDate,
                    formal?.DevelopmentPdcDate,
                    record?.CompletedOn,
                    record?.CompletedYear,
                    record?.CompletedMonth);
            });

        return Build(projects, facts, name => rootOrder.TryGetValue(name, out var order) ? order : int.MaxValue, asOf);
    }

    /// <summary>The recorded facts one project's band is worked out from.</summary>
    internal sealed record ProjectStandingFacts(
        string? ParentCategory,
        DateOnly? AonDate,
        IReadOnlyCollection<StageStatus> AonStageStatuses,
        DateOnly? SupplyOrderDate,
        DateOnly? DevelopmentPdcDate,
        DateOnly? CompletedOn,
        int? CompletedYear,
        short? CompletedMonth);

    /// <summary>The lines and category order from facts already read; no database access.</summary>
    internal static ProjectBriefingStageDistributionData Build(
        IReadOnlyList<ProjectBriefingPresentationProject> projects,
        IReadOnlyDictionary<int, ProjectStandingFacts> facts,
        Func<string, int> categoryRank,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(categoryRank);

        var lines = new List<ArppProjectLineVm>(projects.Count);
        foreach (var project in projects.GroupBy(project => project.ProjectId).Select(group => group.First()))
        {
            var fact = facts.GetValueOrDefault(project.ProjectId)
                ?? new ProjectStandingFacts(null, null, Array.Empty<StageStatus>(), null, null, null, null, null);
            var lifecycle = ProjectProgressBands.LifecycleOf(project.LifecycleStatus);
            // A presented project is never deleted or archived, so an active one is in work.
            var inWork = lifecycle == Services.Stages.LifecycleAsOf.Active;
            var stageCode = inWork && !string.IsNullOrWhiteSpace(project.PresentStageCode) ? project.PresentStageCode.Trim() : null;
            var pastAon = ProjectProgressBands.IsPastAon(fact.AonDate, fact.AonStageStatuses, inWork, stageCode);
            var supplyOrderPlaced = inWork && ProjectProgressBands.HasSupplyOrderEvidence(fact.SupplyOrderDate, project.LifecycleStatus, stageCode);
            var band = ProjectProgressBands.Resolve(lifecycle, inWork, pastAon, supplyOrderPlaced);

            var atDevelopment = band == ArppProgressBucket.UnderDevelopment
                && string.Equals(stageCode, StageCodes.DEVP, StringComparison.OrdinalIgnoreCase);
            var pdc = atDevelopment ? fact.DevelopmentPdcDate : null;
            var completed = band == ArppProgressBucket.Completed;
            lines.Add(new ArppProjectLineVm(
                project.ProjectId,
                project.ProjectName,
                string.IsNullOrWhiteSpace(fact.ParentCategory) ? NotCategorised : fact.ParentCategory.Trim(),
                band,
                stageCode,
                inWork ? project.PresentStage : null,
                stageCode is null ? -1 : Array.FindIndex(StageCodes.All, code => string.Equals(code, stageCode, StringComparison.OrdinalIgnoreCase)),
                pdc,
                pdc is { } due && due < asOf,
                completed ? ProjectProgressBands.CompletionLabel(fact.CompletedOn, fact.CompletedYear, fact.CompletedMonth) : null,
                completed ? ProjectProgressBands.CompletionSort(fact.CompletedOn, fact.CompletedYear, fact.CompletedMonth) : null));
        }

        // Parent categories in their own order (as Analytics › ARPP lists them); others after, by name.
        var categoryOrder = lines
            .Select(line => line.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => string.Equals(name, NotCategorised, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(categoryRank)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProjectBriefingStageDistributionData
        {
            AsOf = asOf,
            CategoryOrder = categoryOrder,
            Lines = lines
        };
    }
}
