using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationTrackerReadService
{
    private readonly ApplicationDbContext _db;

    public ProliferationTrackerReadService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<ProliferationTrackerRow>> GetAsync(
        ProliferationTrackerFilter filter,
        CancellationToken cancellationToken = default)
    {
        filter ??= new ProliferationTrackerFilter();

        var projectQuery = BuildProjectQuery(filter);

        var yearlySnapshots = await LoadYearlySnapshotsAsync(filter, projectQuery, cancellationToken);
        var granularSnapshots = await LoadGranularSnapshotsAsync(filter, projectQuery, cancellationToken);

        if (yearlySnapshots.Count == 0 && granularSnapshots.Count == 0)
        {
            return Array.Empty<ProliferationTrackerRow>();
        }

        var projectMetadata = BuildProjectMetadata(yearlySnapshots, granularSnapshots);
        var yearlyMap = yearlySnapshots.ToDictionary(static s => s.Key);
        var granularMap = granularSnapshots.ToDictionary(static s => s.Key);

        var keys = yearlyMap.Keys
            .Concat(granularMap.Keys)
            .Distinct()
            .ToList();
        keys.Sort((left, right) => CompareKeys(left, right, projectMetadata));

        var preferenceMap = await LoadPreferencesAsync(filter, projectMetadata.Keys, cancellationToken);

        var rows = new List<ProliferationTrackerRow>(keys.Count);
        foreach (var key in keys)
        {
            yearlyMap.TryGetValue(key, out var yearlySnapshot);
            granularMap.TryGetValue(key, out var granularSnapshot);

            if (!projectMetadata.TryGetValue(key.ProjectId, out var metadata))
            {
                continue;
            }

            var yearlyMetrics = yearlySnapshot?.Metrics;
            var granularMetrics = granularSnapshot?.Metrics;

            var preference = DeterminePreference(key, yearlyMetrics, granularMetrics, preferenceMap);
            var effectiveMetrics = DetermineEffective(yearlyMetrics, granularMetrics, preference.Mode);
            var varianceMetrics = ComputeVariance(yearlyMetrics, granularMetrics);

            rows.Add(new ProliferationTrackerRow(
                metadata.ProjectId,
                metadata.ProjectName,
                metadata.SponsoringUnitId,
                metadata.SponsoringUnitName,
                metadata.SimulatorUserId,
                metadata.SimulatorDisplayName,
                key.Source,
                key.Year,
                ToDto(yearlyMetrics),
                ToDto(granularMetrics),
                ToDto(effectiveMetrics),
                ToDto(varianceMetrics),
                new ProliferationPreferenceMetadata(
                    preference.Mode,
                    preference.PreferredYear,
                    preference.MatchesPreferredYear,
                    preference.RowVersion)));
        }

        return rows;
    }

    private IQueryable<Project> BuildProjectQuery(ProliferationTrackerFilter filter)
    {
        var projectQuery = _db.Projects.AsNoTracking();

        if (filter.ProjectId.HasValue)
        {
            projectQuery = projectQuery.Where(p => p.Id == filter.ProjectId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.ProjectSearchTerm))
        {
            var term = filter.ProjectSearchTerm.Trim();
            if (!string.IsNullOrEmpty(term))
            {
                var pattern = $"%{EscapeLikePattern(term)}%";
                projectQuery = projectQuery.Where(p => EF.Functions.ILike(p.Name, pattern, "\\"));
            }
        }

        if (filter.SponsoringUnitId.HasValue)
        {
            projectQuery = projectQuery.Where(p => p.SponsoringUnitId == filter.SponsoringUnitId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.SimulatorUserId))
        {
            var simulator = filter.SimulatorUserId.Trim();
            if (!string.IsNullOrEmpty(simulator))
            {
                projectQuery = projectQuery.Where(p => p.LeadPoUserId == simulator);
            }
        }

        return projectQuery;
    }

    private async Task<List<YearlySnapshot>> LoadYearlySnapshotsAsync(
        ProliferationTrackerFilter filter,
        IQueryable<Project> projectQuery,
        CancellationToken cancellationToken)
    {
        var yearlyQuery = _db.ProliferationYearlies.AsNoTracking();

        if (filter.Source.HasValue)
        {
            var source = filter.Source.Value;
            yearlyQuery = yearlyQuery.Where(y => y.Source == source);
        }

        if (filter.Year.HasValue)
        {
            var year = filter.Year.Value;
            yearlyQuery = yearlyQuery.Where(y => y.Year == year);
        }

        if (filter.ProjectId.HasValue)
        {
            var projectId = filter.ProjectId.Value;
            yearlyQuery = yearlyQuery.Where(y => y.ProjectId == projectId);
        }

        return await (
                from y in yearlyQuery
                join p in projectQuery on y.ProjectId equals p.Id
                select new YearlySnapshot(
                    new AggregationKey(y.ProjectId, y.Source, y.Year),
                    new MetricValues(
                        y.Metrics.DirectBeneficiaries,
                        y.Metrics.IndirectBeneficiaries,
                        y.Metrics.InvestmentValue),
                    new ProjectMetadata(
                        p.Id,
                        p.Name,
                        p.SponsoringUnitId,
                        p.SponsoringUnit != null ? p.SponsoringUnit.Name : null,
                        p.LeadPoUserId,
                        p.LeadPoUser != null ? p.LeadPoUser.FullName : null,
                        p.LeadPoUser != null ? p.LeadPoUser.UserName : null)))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<GranularSnapshot>> LoadGranularSnapshotsAsync(
        ProliferationTrackerFilter filter,
        IQueryable<Project> projectQuery,
        CancellationToken cancellationToken)
    {
        var granularQuery = _db.ProliferationGranularYearlyView.AsNoTracking();

        if (filter.Source.HasValue)
        {
            var source = filter.Source.Value;
            granularQuery = granularQuery.Where(g => g.Source == source);
        }

        if (filter.Year.HasValue)
        {
            var year = filter.Year.Value;
            granularQuery = granularQuery.Where(g => g.Year == year);
        }

        if (filter.ProjectId.HasValue)
        {
            var projectId = filter.ProjectId.Value;
            granularQuery = granularQuery.Where(g => g.ProjectId == projectId);
        }

        return await (
                from g in granularQuery
                join p in projectQuery on g.ProjectId equals p.Id
                select new GranularSnapshot(
                    new AggregationKey(g.ProjectId, g.Source, g.Year),
                    new MetricValues(g.DirectBeneficiaries, g.IndirectBeneficiaries, g.InvestmentValue),
                    new ProjectMetadata(
                        p.Id,
                        p.Name,
                        p.SponsoringUnitId,
                        p.SponsoringUnit != null ? p.SponsoringUnit.Name : null,
                        p.LeadPoUserId,
                        p.LeadPoUser != null ? p.LeadPoUser.FullName : null,
                        p.LeadPoUser != null ? p.LeadPoUser.UserName : null)))
            .ToListAsync(cancellationToken);
    }

    private static Dictionary<int, ProjectMetadata> BuildProjectMetadata(
        IEnumerable<YearlySnapshot> yearlySnapshots,
        IEnumerable<GranularSnapshot> granularSnapshots)
    {
        var result = new Dictionary<int, ProjectMetadata>();

        foreach (var snapshot in yearlySnapshots)
        {
            result.TryAdd(snapshot.Project.ProjectId, snapshot.Project);
        }

        foreach (var snapshot in granularSnapshots)
        {
            result.TryAdd(snapshot.Project.ProjectId, snapshot.Project);
        }

        return result;
    }

    private async Task<Dictionary<(int ProjectId, ProliferationSource Source), PreferenceSnapshot>> LoadPreferencesAsync(
        ProliferationTrackerFilter filter,
        IEnumerable<int> projectIds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filter.UserId))
        {
            return new Dictionary<(int, ProliferationSource), PreferenceSnapshot>();
        }

        var idArray = projectIds.Distinct().ToArray();
        if (idArray.Length == 0)
        {
            return new Dictionary<(int, ProliferationSource), PreferenceSnapshot>();
        }

        var query = _db.ProliferationYearPreferences
            .AsNoTracking()
            .Where(p => p.UserId == filter.UserId);

        if (filter.Source.HasValue)
        {
            var source = filter.Source.Value;
            query = query.Where(p => p.Source == source);
        }

        query = query.Where(p => idArray.Contains(p.ProjectId));

        var preferences = await query
            .Select(p => new PreferenceSnapshot(p.ProjectId, p.Source, p.Year, p.RowVersion))
            .ToListAsync(cancellationToken);

        var result = new Dictionary<(int, ProliferationSource), PreferenceSnapshot>();
        foreach (var preference in preferences)
        {
            result[(preference.ProjectId, preference.Source)] = preference;
        }

        return result;
    }

    private static PreferenceSnapshotResult DeterminePreference(
        AggregationKey key,
        MetricValues? yearly,
        MetricValues? granular,
        IReadOnlyDictionary<(int ProjectId, ProliferationSource Source), PreferenceSnapshot> preferenceMap)
    {
        if (preferenceMap.TryGetValue((key.ProjectId, key.Source), out var preference))
        {
            if (preference.Year == key.Year)
            {
                if (yearly is not null)
                {
                    return new PreferenceSnapshotResult(ProliferationPreferenceMode.UseYearly, preference.Year, true, preference.RowVersion);
                }

                if (granular is not null)
                {
                    return new PreferenceSnapshotResult(ProliferationPreferenceMode.UseGranular, preference.Year, true, preference.RowVersion);
                }

                return new PreferenceSnapshotResult(ProliferationPreferenceMode.Auto, preference.Year, true, preference.RowVersion);
            }

            return new PreferenceSnapshotResult(ProliferationPreferenceMode.Auto, preference.Year, false, preference.RowVersion);
        }

        return new PreferenceSnapshotResult(ProliferationPreferenceMode.Auto, null, false, null);
    }

    private static MetricValues? DetermineEffective(
        MetricValues? yearly,
        MetricValues? granular,
        ProliferationPreferenceMode mode)
    {
        return mode switch
        {
            ProliferationPreferenceMode.UseYearly => yearly ?? granular,
            ProliferationPreferenceMode.UseGranular => granular ?? yearly,
            _ => yearly ?? granular
        };
    }

    private static MetricValues? ComputeVariance(MetricValues? yearly, MetricValues? granular)
    {
        if (yearly is null || granular is null)
        {
            return null;
        }

        var yearlyValue = yearly.Value;
        var granularValue = granular.Value;

        var direct = Subtract(yearlyValue.DirectBeneficiaries, granularValue.DirectBeneficiaries);
        var indirect = Subtract(yearlyValue.IndirectBeneficiaries, granularValue.IndirectBeneficiaries);
        var investment = Subtract(yearlyValue.InvestmentValue, granularValue.InvestmentValue);

        if (direct is null && indirect is null && investment is null)
        {
            return null;
        }

        return new MetricValues(direct, indirect, investment);
    }

    private static int? Subtract(int? left, int? right)
    {
        if (!left.HasValue || !right.HasValue)
        {
            return null;
        }

        return left.Value - right.Value;
    }

    private static decimal? Subtract(decimal? left, decimal? right)
    {
        if (!left.HasValue || !right.HasValue)
        {
            return null;
        }

        return left.Value - right.Value;
    }

    private static ProliferationMetricsDto? ToDto(MetricValues? metrics)
    {
        return metrics is null
            ? null
            : new ProliferationMetricsDto(
                metrics.Value.DirectBeneficiaries,
                metrics.Value.IndirectBeneficiaries,
                metrics.Value.InvestmentValue);
    }

    private static int CompareKeys(
        AggregationKey left,
        AggregationKey right,
        IReadOnlyDictionary<int, ProjectMetadata> projectMetadata)
    {
        var leftProject = projectMetadata[left.ProjectId];
        var rightProject = projectMetadata[right.ProjectId];

        var comparison = string.Compare(leftProject.ProjectName, rightProject.ProjectName, StringComparison.OrdinalIgnoreCase);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.Source.CompareTo(right.Source);
        if (comparison != 0)
        {
            return comparison;
        }

        return left.Year.CompareTo(right.Year);
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    private sealed record ProjectMetadata(
        int ProjectId,
        string ProjectName,
        int? SponsoringUnitId,
        string? SponsoringUnitName,
        string? SimulatorUserId,
        string? SimulatorFullName,
        string? SimulatorUserName)
    {
        public string? SimulatorDisplayName => !string.IsNullOrWhiteSpace(SimulatorFullName)
            ? SimulatorFullName
            : !string.IsNullOrWhiteSpace(SimulatorUserName)
                ? SimulatorUserName
                : SimulatorUserId;
    }

    private sealed record YearlySnapshot(AggregationKey Key, MetricValues Metrics, ProjectMetadata Project);

    private sealed record GranularSnapshot(AggregationKey Key, MetricValues Metrics, ProjectMetadata Project);

    private readonly record struct MetricValues
    {
        public int? DirectBeneficiaries { get; init; }
        public int? IndirectBeneficiaries { get; init; }
        public decimal? InvestmentValue { get; init; }

        public MetricValues(int? directBeneficiaries, int? indirectBeneficiaries, decimal? investmentValue)
        {
            DirectBeneficiaries = directBeneficiaries;
            IndirectBeneficiaries = indirectBeneficiaries;
            InvestmentValue = investmentValue;
        }
    }

    private readonly record struct AggregationKey(int ProjectId, ProliferationSource Source, int Year);

    private readonly record struct PreferenceSnapshot(int ProjectId, ProliferationSource Source, int Year, byte[] RowVersion);

    private readonly record struct PreferenceSnapshotResult(
        ProliferationPreferenceMode Mode,
        int? PreferredYear,
        bool MatchesPreferredYear,
        byte[]? RowVersion);
}

public enum ProliferationPreferenceMode
{
    Auto = 0,
    UseYearly = 1,
    UseGranular = 2
}

public sealed record ProliferationMetricsDto(
    int? DirectBeneficiaries,
    int? IndirectBeneficiaries,
    decimal? InvestmentValue);

public sealed record ProliferationPreferenceMetadata(
    ProliferationPreferenceMode Mode,
    int? PreferredYear,
    bool PreferredYearMatches,
    byte[]? RowVersion)
{
    public bool HasPreference => PreferredYear.HasValue;
    public bool MatchesPreferredYear => PreferredYearMatches;
}

public sealed record ProliferationTrackerRow(
    int ProjectId,
    string ProjectName,
    int? SponsoringUnitId,
    string? SponsoringUnitName,
    string? SimulatorUserId,
    string? SimulatorDisplayName,
    ProliferationSource Source,
    int Year,
    ProliferationMetricsDto? Yearly,
    ProliferationMetricsDto? GranularSum,
    ProliferationMetricsDto? Effective,
    ProliferationMetricsDto? Variance,
    ProliferationPreferenceMetadata Preference);

public sealed record ProliferationTrackerFilter
{
    public int? ProjectId { get; init; }
    public string? ProjectSearchTerm { get; init; }
    public ProliferationSource? Source { get; init; }
    public int? Year { get; init; }
    public int? SponsoringUnitId { get; init; }
    public string? SimulatorUserId { get; init; }
    public string? UserId { get; init; }
}
