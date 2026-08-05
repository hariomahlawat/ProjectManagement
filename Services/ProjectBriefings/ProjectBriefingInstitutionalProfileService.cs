using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services;

namespace ProjectManagement.Services.ProjectBriefings;

public interface IProjectBriefingInstitutionalProfileService
{
    Task<ProjectBriefingInstitutionalProfileData?> BuildAsync(
        ProjectBriefingInstitutionalProfileOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds the single authoritative institutional snapshot used by the SDD profile slide.
/// ERP-backed metrics are read-only; only the authorised history, partnership and optional
/// user-authored footer-strip content is carried into the presentation.
/// </summary>
public sealed class ProjectBriefingInstitutionalProfileService : IProjectBriefingInstitutionalProfileService
{
    private readonly ApplicationDbContext _db;
    private readonly ProliferationAggregateReadService _proliferation;
    private readonly TrainingTrackerReadService _training;
    private readonly IClock _clock;

    public ProjectBriefingInstitutionalProfileService(
        ApplicationDbContext db,
        ProliferationAggregateReadService proliferation,
        TrainingTrackerReadService training,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _proliferation = proliferation ?? throw new ArgumentNullException(nameof(proliferation));
        _training = training ?? throw new ArgumentNullException(nameof(training));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ProjectBriefingInstitutionalProfileData?> BuildAsync(
        ProjectBriefingInstitutionalProfileOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalized = ProjectBriefingInstitutionalProfileOptions.Normalize(
            options.IncludeSlide,
            options.Title,
            options.IncludeHistory,
            options.HistoryMilestones,
            options.Modules,
            options.ProjectScope,
            options.MaximumDetailRows,
            options.TrainingHighlightTechnicalCategory,
            options.PartnershipEntries,
            options.IncludeFooterStrip,
            options.FooterStripText,
            options.FooterStripEmphasisValue,
            options.FooterStripStyle,
            options.FooterStripAlignment);

        if (!normalized.IncludeSlide)
        {
            return null;
        }

        var modules = new List<ProjectBriefingInstitutionalModuleData>(normalized.Modules.Count);
        foreach (var module in normalized.Modules)
        {
            var item = module switch
            {
                ProjectBriefingInstitutionalProfileModule.ProjectsDeveloped =>
                    await BuildProjectsAsync(
                        normalized.ProjectScope,
                        normalized.MaximumDetailRows,
                        cancellationToken),
                ProjectBriefingInstitutionalProfileModule.Proliferation =>
                    await BuildProliferationAsync(normalized.MaximumDetailRows, cancellationToken),
                ProjectBriefingInstitutionalProfileModule.TrainingSupport =>
                    await BuildTrainingAsync(
                        normalized.MaximumDetailRows,
                        normalized.TrainingHighlightTechnicalCategory,
                        cancellationToken),
                ProjectBriefingInstitutionalProfileModule.IntellectualProperty =>
                    await BuildIprAsync(cancellationToken),
                ProjectBriefingInstitutionalProfileModule.Partnerships =>
                    BuildPartnerships(normalized.PartnershipEntries),
                _ => null
            };

            if (item is not null)
            {
                modules.Add(item);
            }
        }

        return new ProjectBriefingInstitutionalProfileData
        {
            Title = normalized.Title,
            HistoryMilestones = normalized.IncludeHistory
                ? normalized.HistoryMilestones
                : Array.Empty<ProjectBriefingInstitutionalHistoryMilestone>(),
            Modules = modules,
            IncludeFooterStrip = normalized.IncludeFooterStrip,
            FooterStripText = normalized.FooterStripText,
            FooterStripEmphasisValue = normalized.FooterStripEmphasisValue,
            FooterStripStyle = normalized.FooterStripStyle,
            FooterStripAlignment = normalized.FooterStripAlignment,
            DataAsOnUtc = _clock.UtcNow.ToUniversalTime()
        };
    }

    private async Task<ProjectBriefingInstitutionalModuleData> BuildProjectsAsync(
        ProjectBriefingInstitutionalProjectScope scope,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        var projects = _db.Projects
            .AsNoTracking()
            .Where(project =>
                !project.IsDeleted
                && !project.IsArchived
                && project.LifecycleStatus == ProjectLifecycleStatus.Completed);

        if (scope == ProjectBriefingInstitutionalProjectScope.OriginalCompleted)
        {
            projects = projects.Where(project => !project.IsBuild);
        }

        var rows = await projects
            .GroupBy(project => project.TechnicalCategory != null
                ? project.TechnicalCategory.Name
                : "Uncategorised")
            .Select(group => new CountRow(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

        return new ProjectBriefingInstitutionalModuleData(
            ProjectBriefingInstitutionalProfileModule.ProjectsDeveloped,
            "Simulators/Projects Developed",
            FormatNumber(rows.Sum(row => row.Value)),
            CompactRows(rows, maximumRows));
    }

    private async Task<ProjectBriefingInstitutionalModuleData> BuildProliferationAsync(
        int maximumRows,
        CancellationToken cancellationToken)
    {
        var aggregates = await _proliferation.GetApprovedAggregatesAsync(
            projectId: null,
            cancellationToken);
        var active = aggregates.Where(row => row.ReportedTotal > 0).ToArray();
        var grouped = active
            .GroupBy(row => string.IsNullOrWhiteSpace(row.TechnicalCategoryName)
                ? "Uncategorised"
                : row.TechnicalCategoryName!)
            .Select(group => new CountRow(group.Key, group.Sum(row => row.ReportedTotal)))
            .ToArray();

        return new ProjectBriefingInstitutionalModuleData(
            ProjectBriefingInstitutionalProfileModule.Proliferation,
            "Proliferated",
            FormatNumber(active.Sum(row => row.ReportedTotal)),
            CompactRows(grouped, maximumRows));
    }

    private async Task<ProjectBriefingInstitutionalModuleData> BuildTrainingAsync(
        int maximumRows,
        string highlightTechnicalCategory,
        CancellationToken cancellationToken)
    {
        var kpis = await _training.GetKpisAsync(query: null, cancellationToken);
        var highlight = await BuildTrainingHighlightAsync(
            highlightTechnicalCategory,
            cancellationToken);

        var reserveForHighlight = string.IsNullOrWhiteSpace(highlight) ? 0 : 1;
        var yearRows = kpis.ByTrainingYear
            .TakeLast(Math.Max(1, maximumRows - reserveForHighlight))
            .Select(year => new ProjectBriefingInstitutionalMetricRow(
                $"FY {year.TrainingYearLabel}",
                FormatNumber(year.TotalTrainees)))
            .ToList();

        return new ProjectBriefingInstitutionalModuleData(
            ProjectBriefingInstitutionalProfileModule.TrainingSupport,
            "Assistance to Field Formations",
            FormatNumber(kpis.TotalTrainees),
            yearRows,
            highlight);
    }

    private async Task<string?> BuildTrainingHighlightAsync(
        string technicalCategory,
        CancellationToken cancellationToken)
    {
        var requested = NormalizeCategory(technicalCategory);
        if (string.IsNullOrWhiteSpace(requested))
        {
            return null;
        }

        var categories = await _db.TechnicalCategories
            .AsNoTracking()
            .Select(category => new { category.Id, category.Name })
            .ToListAsync(cancellationToken);
        var matches = categories
            .Where(category => string.Equals(
                NormalizeCategory(category.Name),
                requested,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        var categoryIds = matches.Select(category => category.Id).ToArray();
        var trainingIds = await _db.TrainingProjects
            .AsNoTracking()
            .Where(link => link.Project != null
                && link.Project.TechnicalCategoryId.HasValue
                && categoryIds.Contains(link.Project.TechnicalCategoryId.Value))
            .Select(link => link.TrainingId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (trainingIds.Length == 0)
        {
            return null;
        }

        var individuals = await _db.Trainings
            .AsNoTracking()
            .Where(training => trainingIds.Contains(training.Id))
            .SumAsync(
                training => training.Counters != null
                    ? training.Counters.Total
                    : training.LegacyOfficerCount
                        + training.LegacyJcoCount
                        + training.LegacyOrCount,
                cancellationToken);

        var unitNames = await _db.TrainingTrainees
            .AsNoTracking()
            .Where(trainee => trainingIds.Contains(trainee.TrainingId)
                && trainee.UnitName != string.Empty)
            .Select(trainee => trainee.UnitName)
            .ToListAsync(cancellationToken);
        var units = unitNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var displayCategory = matches[0].Name;

        return units > 0
            ? $"{FormatNumber(units)} Units / {FormatNumber(individuals)} Individuals trained in {displayCategory}"
            : $"{FormatNumber(individuals)} Individuals trained in {displayCategory}";
    }

    private async Task<ProjectBriefingInstitutionalModuleData> BuildIprAsync(
        CancellationToken cancellationToken)
    {
        var rows = await _db.IprRecords
            .AsNoTracking()
            .GroupBy(record => new { record.Type, record.Status })
            .Select(group => new IprCountRow(group.Key.Type, group.Key.Status, group.Count()))
            .ToListAsync(cancellationToken);

        var patentsGranted = Count(rows, IprType.Patent, IprStatus.Granted);
        var copyrightsRegistered = Count(rows, IprType.Copyright, IprStatus.Granted);
        var patentsFiled = rows
            .Where(row => row.Type == IprType.Patent
                && row.Status is IprStatus.Filed or IprStatus.FilingUnderProcess)
            .Sum(row => row.Value);
        var protectedTotal = patentsGranted + copyrightsRegistered;

        return new ProjectBriefingInstitutionalModuleData(
            ProjectBriefingInstitutionalProfileModule.IntellectualProperty,
            "Intellectual Property",
            FormatNumber(protectedTotal),
            new[]
            {
                new ProjectBriefingInstitutionalMetricRow("Patents granted", FormatNumber(patentsGranted)),
                new ProjectBriefingInstitutionalMetricRow("Copyrights registered", FormatNumber(copyrightsRegistered)),
                new ProjectBriefingInstitutionalMetricRow("Patents filed", FormatNumber(patentsFiled))
            });
    }

    private static ProjectBriefingInstitutionalModuleData BuildPartnerships(
        IReadOnlyList<string> partnerships)
        => new(
            ProjectBriefingInstitutionalProfileModule.Partnerships,
            "Military–Academia–Industry Synergy",
            Headline: null,
            Rows: partnerships
                .Select(item => new ProjectBriefingInstitutionalMetricRow(item, string.Empty))
                .ToArray());

    private static IReadOnlyList<ProjectBriefingInstitutionalMetricRow> CompactRows(
        IEnumerable<CountRow> source,
        int maximumRows)
    {
        var ordered = source
            .Where(row => row.Value > 0)
            .OrderByDescending(row => row.Value)
            .ThenBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ordered.Length <= maximumRows)
        {
            return ordered
                .Select(row => new ProjectBriefingInstitutionalMetricRow(row.Label, FormatNumber(row.Value)))
                .ToArray();
        }

        var visibleCount = Math.Max(1, maximumRows - 1);
        var result = ordered
            .Take(visibleCount)
            .Select(row => new ProjectBriefingInstitutionalMetricRow(row.Label, FormatNumber(row.Value)))
            .ToList();
        result.Add(new ProjectBriefingInstitutionalMetricRow(
            "Others",
            FormatNumber(ordered.Skip(visibleCount).Sum(row => row.Value))));
        return result;
    }

    private static int Count(
        IEnumerable<IprCountRow> rows,
        IprType type,
        IprStatus status)
        => rows.FirstOrDefault(row => row.Type == type && row.Status == status)?.Value ?? 0;

    private static string NormalizeCategory(string? value)
        => (value ?? string.Empty)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static string FormatNumber(int value)
        => value.ToString("N0", CultureInfo.InvariantCulture);

    private sealed record CountRow(string Label, int Value);
    private sealed record IprCountRow(IprType Type, IprStatus Status, int Value);
}
