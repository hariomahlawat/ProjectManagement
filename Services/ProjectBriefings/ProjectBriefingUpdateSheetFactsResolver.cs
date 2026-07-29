using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.ProjectBriefings;

public interface IProjectBriefingUpdateSheetFactsResolver
{
    Task<IReadOnlyDictionary<int, ProjectBriefingUpdateSheetFacts>> ResolveAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default);
}

public sealed record ProjectBriefingUpdateSheetFacts(
    string? ArppReference,
    string? Fund,
    string? DfpdsSchedule,
    string? Cfa,
    DateOnly? AonDate,
    DateOnly? SupplyOrderDate,
    DateOnly? DevelopmentPdcDate,
    IReadOnlyList<string> JdpNames,
    string? ProjectOfficer,
    bool ProjectOfficerIsComplete,
    string? LineDirectorate,
    bool IsDelistedArppPosition)
{
    public bool HasCompleteArppDetails =>
        (IsDelistedArppPosition || !string.IsNullOrWhiteSpace(ArppReference))
        && !string.IsNullOrWhiteSpace(Fund)
        && !string.IsNullOrWhiteSpace(DfpdsSchedule)
        && !string.IsNullOrWhiteSpace(Cfa);
}

/// <summary>
/// Resolves the authoritative factual fields required by the formal project-update-sheet layout.
/// All reads are performed in bounded batch queries so deck generation does not create an N+1 query pattern.
/// </summary>
public sealed class ProjectBriefingUpdateSheetFactsResolver : IProjectBriefingUpdateSheetFactsResolver
{
    private readonly ApplicationDbContext _db;

    public ProjectBriefingUpdateSheetFactsResolver(ApplicationDbContext db)
        => _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<IReadOnlyDictionary<int, ProjectBriefingUpdateSheetFacts>> ResolveAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default)
    {
        var ids = projectIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<int, ProjectBriefingUpdateSheetFacts>();
        }

        var projectRows = await _db.Projects
            .AsNoTracking()
            .Where(project => ids.Contains(project.Id))
            .Select(project => new
            {
                project.Id,
                OfficerRank = project.LeadPoUser != null ? project.LeadPoUser.Rank : null,
                OfficerFullName = project.LeadPoUser != null ? project.LeadPoUser.FullName : null,
                LineDirectorate = project.SponsoringLineDirectorate != null
                    ? project.SponsoringLineDirectorate.Name
                    : null
            })
            .ToListAsync(cancellationToken);

        var stageRows = await _db.ProjectStages
            .AsNoTracking()
            .Where(stage => ids.Contains(stage.ProjectId)
                && (stage.StageCode == StageCodes.AON || stage.StageCode == StageCodes.DEVP))
            .Select(stage => new
            {
                stage.Id,
                stage.ProjectId,
                stage.StageCode,
                stage.SortOrder,
                stage.CompletedOn,
                stage.PlannedDue
            })
            .ToListAsync(cancellationToken);

        var aonDates = stageRows
            .Where(stage => stage.StageCode == StageCodes.AON)
            .GroupBy(stage => stage.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(stage => stage.CompletedOn.HasValue)
                    .ThenByDescending(stage => stage.CompletedOn)
                    .ThenByDescending(stage => stage.SortOrder)
                    .ThenByDescending(stage => stage.Id)
                    .Select(stage => stage.CompletedOn)
                    .FirstOrDefault());

        var developmentPdcs = stageRows
            .Where(stage => stage.StageCode == StageCodes.DEVP)
            .GroupBy(stage => stage.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(stage => stage.PlannedDue.HasValue)
                    .ThenByDescending(stage => stage.PlannedDue)
                    .ThenByDescending(stage => stage.SortOrder)
                    .ThenByDescending(stage => stage.Id)
                    .Select(stage => stage.PlannedDue)
                    .FirstOrDefault());

        var supplyOrderDates = await _db.ProjectSupplyOrderFacts
            .AsNoTracking()
            .Where(fact => ids.Contains(fact.ProjectId))
            .Select(fact => new
            {
                fact.Id,
                fact.ProjectId,
                fact.SupplyOrderDate,
                fact.CreatedOnUtc
            })
            .ToListAsync(cancellationToken);
        var supplyOrderByProject = supplyOrderDates
            .GroupBy(fact => fact.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(fact => fact.CreatedOnUtc)
                    .ThenByDescending(fact => fact.Id)
                    .Select(fact => (DateOnly?)fact.SupplyOrderDate)
                    .FirstOrDefault());

        var jdpRows = await _db.IndustryPartnerProjects
            .AsNoTracking()
            .Where(link => ids.Contains(link.ProjectId))
            .Select(link => new
            {
                link.ProjectId,
                link.IndustryPartner.Name
            })
            .ToListAsync(cancellationToken);
        var jdpsByProject = jdpRows
            .Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .GroupBy(row => row.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(row => Normalize(row.Name))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        var arppRows = await _db.ArppPublishedEntries
            .AsNoTracking()
            .Where(entry => entry.ProjectId.HasValue && ids.Contains(entry.ProjectId.Value))
            .Select(entry => new
            {
                ProjectId = entry.ProjectId!.Value,
                entry.Id,
                entry.Fund,
                entry.DfpdsSchedule,
                entry.Cfa,
                entry.PppNumber,
                entry.Category,
                entry.PublishedIssue.FinancialYearStart,
                entry.PublishedIssue.IssueSequence,
                entry.PublishedIssue.IssueDate,
                entry.PublishedIssue.ArppIssueId
            })
            .ToListAsync(cancellationToken);
        var arppByProject = arppRows
            .GroupBy(row => row.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(row => row.FinancialYearStart)
                    .ThenByDescending(row => row.IssueSequence)
                    .ThenByDescending(row => row.IssueDate)
                    .ThenByDescending(row => row.ArppIssueId)
                    .ThenByDescending(row => row.Id)
                    .First());

        return projectRows.ToDictionary(
            row => row.Id,
            row =>
            {
                arppByProject.TryGetValue(row.Id, out var arpp);
                var officer = FormatOfficer(row.OfficerRank, row.OfficerFullName);
                return new ProjectBriefingUpdateSheetFacts(
                    arpp?.Category == ArppCategory.Delisted ? null : NormalizeNullable(arpp?.PppNumber),
                    NormalizeNullable(arpp?.Fund),
                    NormalizeNullable(arpp?.DfpdsSchedule),
                    NormalizeNullable(arpp?.Cfa),
                    aonDates.GetValueOrDefault(row.Id),
                    supplyOrderByProject.GetValueOrDefault(row.Id),
                    developmentPdcs.GetValueOrDefault(row.Id),
                    jdpsByProject.GetValueOrDefault(row.Id) ?? Array.Empty<string>(),
                    officer.Display,
                    officer.IsComplete,
                    NormalizeNullable(row.LineDirectorate),
                    arpp?.Category == ArppCategory.Delisted);
            });
    }

    private static (string? Display, bool IsComplete) FormatOfficer(string? rank, string? fullName)
    {
        var normalizedRank = NormalizeNullable(rank);
        var normalizedName = NormalizeNullable(fullName);
        if (normalizedName is null)
        {
            return (null, false);
        }

        if (normalizedRank is null)
        {
            return (normalizedName, false);
        }

        var display = normalizedName.StartsWith(normalizedRank + " ", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedName, normalizedRank, StringComparison.OrdinalIgnoreCase)
                ? normalizedName
                : $"{normalizedRank} {normalizedName}";
        return (display, true);
    }

    private static string Normalize(string value)
        => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Normalize(value);
}
