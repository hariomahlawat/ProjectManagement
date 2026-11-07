using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Analytics.Presentation;

// SECTION: Cost facts for the CoE slides' speaker notes
/// <summary>
/// The costs the CoE slides' notes quote, each from its one owner: a project's cost from
/// <see cref="IProjectCostResolver"/> (the Project Overview's figure), and its indicative
/// proliferation cost as recorded on the project (lakh).
/// </summary>
public sealed record CoeSlideCostFacts(
    IReadOnlyDictionary<int, ProjectCostResolution> Costs,
    IReadOnlyDictionary<int, decimal> ProliferationCostLakhs)
{
    public static CoeSlideCostFacts Empty { get; } = new(
        new Dictionary<int, ProjectCostResolution>(),
        new Dictionary<int, decimal>());
}

public interface ICoeSlideCostReader
{
    Task<CoeSlideCostFacts> ReadAsync(IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default);
}

public sealed class CoeSlideCostReader : ICoeSlideCostReader
{
    private readonly ApplicationDbContext _db;
    private readonly IProjectCostResolver _costs;

    public CoeSlideCostReader(ApplicationDbContext db, IProjectCostResolver costs)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _costs = costs ?? throw new ArgumentNullException(nameof(costs));
    }

    public async Task<CoeSlideCostFacts> ReadAsync(IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default)
    {
        var ids = (projectIds ?? Array.Empty<int>()).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return CoeSlideCostFacts.Empty;
        }

        var costs = await _costs.ResolveCostInCrAsync(ids, cancellationToken);
        var proliferation = await _db.ProjectProductionCostFacts
            .AsNoTracking()
            .Where(fact => ids.Contains(fact.ProjectId) && fact.ApproxProductionCost != null && fact.ApproxProductionCost >= 0m)
            .Select(fact => new { fact.ProjectId, Cost = fact.ApproxProductionCost!.Value })
            .ToListAsync(cancellationToken);

        return new CoeSlideCostFacts(costs, proliferation.ToDictionary(row => row.ProjectId, row => row.Cost));
    }
}
// END SECTION

// SECTION: Speaker notes text
/// <summary>The two slides' speaker notes: slide 1 each project's cost, slide 2 each AR/VR project's proliferation.</summary>
public static class CoeSlideNotesBuilder
{
    /// <summary>Each project on slide 1 with its latest cost, column by column, then the total of the costs shown.</summary>
    public static string ProjectsNotes(IReadOnlyList<CoeSlideColumn> columns, CoeSlideCostFacts facts)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(facts);
        if (columns.Count == 0)
        {
            return string.Empty;
        }

        var notes = new StringBuilder();
        notes.AppendLine("Latest cost of each project, as on its Project Overview: PNC, then L1; then, for a completed project, its recorded cost, otherwise AoN, then IPA.");

        var total = 0m;
        var withCost = 0;
        var projects = 0;
        foreach (var column in columns)
        {
            notes.AppendLine();
            notes.AppendLine($"{column.Name} ({column.Projects.Count})");
            foreach (var project in column.Projects)
            {
                projects++;
                var cost = project.ProjectId is { } id && facts.Costs.TryGetValue(id, out var resolution) && resolution.CostInCr is > 0m
                    ? resolution
                    : null;
                if (cost is not null)
                {
                    total += cost.CostInCr!.Value;
                    withCost++;
                }

                notes.AppendLine($"• {project.Name}: {(cost is null ? "cost not recorded" : $"{Money(cost.CostInLakhs)} ({cost.SourceLabel})")}");
            }
        }

        notes.AppendLine();
        notes.Append(withCost == 0
            ? "No project on this slide has a cost recorded yet."
            : $"Total of the costs shown: {Money(total * 100m)} ({withCost} of {projects} {Plural(projects, "project")} {(withCost == 1 ? "has" : "have")} a cost).");
        return notes.ToString();
    }

    /// <summary>
    /// Each completed AR/VR project (slide 2's list, in its order) with its units proliferated and its
    /// proliferation cost, then any other AR/VR project with proliferation recorded. Units appear only
    /// when <paramref name="proliferation"/> is given (the caller may see the proliferation tracker).
    /// </summary>
    public static string SummaryNotes(
        IReadOnlyList<CoeSlideProject> completed,
        IReadOnlyList<ArVrProjectProliferationVm>? proliferation,
        CoeSlideCostFacts facts)
    {
        ArgumentNullException.ThrowIfNull(completed);
        ArgumentNullException.ThrowIfNull(facts);

        var unitsByProject = proliferation?.ToDictionary(item => item.ProjectId) ?? new Dictionary<int, ArVrProjectProliferationVm>();
        var notes = new StringBuilder();
        notes.AppendLine(proliferation is null
            ? "Indicative proliferation cost of each completed AR/VR project. Units proliferated are shown to the proliferation tracker's viewers only."
            : "Proliferation of each completed AR/VR project: units delivered over all years (approved proliferation tracker figures, SDD and 515 ABW), and its indicative proliferation cost.");

        if (completed.Count > 0)
        {
            notes.AppendLine();
            foreach (var project in completed)
            {
                var year = project.Tag == "—" ? string.Empty : $" ({project.Tag})";
                notes.AppendLine($"• {project.Name}{year}: {ProliferationLine(project.ProjectId, unitsByProject, proliferation is not null, facts)}");
            }
        }

        var completedIds = completed.Select(project => project.ProjectId).OfType<int>().ToHashSet();
        var others = (proliferation ?? Array.Empty<ArVrProjectProliferationVm>())
            .Where(item => item.Total > 0 && !completedIds.Contains(item.ProjectId))
            .ToList();
        if (others.Count > 0)
        {
            notes.AppendLine();
            notes.AppendLine("Other AR/VR projects with proliferation recorded:");
            foreach (var item in others)
            {
                notes.AppendLine($"• {item.Name}: {ProliferationLine(item.ProjectId, unitsByProject, true, facts)}");
            }
        }

        if (proliferation is not null)
        {
            var units = proliferation.Sum(item => item.Total);
            notes.AppendLine();
            notes.Append($"Total: {units.ToString("N0", CultureInfo.InvariantCulture)} {Plural(units, "unit")} proliferated.");
        }

        return notes.ToString().TrimEnd();
    }

    private static string ProliferationLine(
        int? projectId,
        IReadOnlyDictionary<int, ArVrProjectProliferationVm> unitsByProject,
        bool showUnits,
        CoeSlideCostFacts facts)
    {
        var cost = projectId is { } costId && facts.ProliferationCostLakhs.TryGetValue(costId, out var lakhs)
            ? $"proliferation cost {Money(lakhs)}"
            : "proliferation cost not recorded";
        if (!showUnits)
        {
            return cost;
        }

        var units = projectId is { } id && unitsByProject.TryGetValue(id, out var item) && item.Total > 0
            ? $"{item.Total.ToString("N0", CultureInfo.InvariantCulture)} {Plural(item.Total, "unit")} proliferated{Sources(item)}"
            : "none proliferated";
        return $"{units} · {cost}";
    }

    // Only when both sources delivered: "(SDD 10, 515 ABW 2)".
    private static string Sources(ArVrProjectProliferationVm item) =>
        item.Sdd > 0 && item.Abw515 > 0
            ? $" (SDD {item.Sdd.ToString("N0", CultureInfo.InvariantCulture)}, 515 ABW {item.Abw515.ToString("N0", CultureInfo.InvariantCulture)})"
            : string.Empty;

    // The Project Overview's money format: "₹1.25 Cr" from 100 lakh up, otherwise "₹45 lakh".
    private static string Money(decimal? lakhs) => ProjectProliferationProfileVm.FormatCost(lakhs);

    private static string Plural(int count, string word) => count == 1 ? word : word + "s";
}
// END SECTION
