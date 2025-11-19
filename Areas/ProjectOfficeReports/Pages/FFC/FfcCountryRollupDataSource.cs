using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

// SECTION: DTO contract
public sealed record FfcCountryRollupDto(
    long CountryId,
    string Iso3,
    string Name,
    int Installed,
    int Delivered,
    int Planned,
    int Total,
    int LatestYear
);

// SECTION: Data source helpers
internal static class FfcCountryRollupDataSource
{
    // SECTION: Aggregation entry point
    public static async Task<IReadOnlyList<FfcCountryRollupDto>> LoadAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var projects = await db.FfcProjects
            .AsNoTracking()
            .Include(project => project.Record)
                .ThenInclude(record => record.Country)
            .Where(project => !project.Record.IsDeleted && project.Record.Country.IsActive)
            .ToListAsync(cancellationToken);

        var aggregates = projects
            .Where(project => project.Record.Country is not null)
            .GroupBy(project => new
            {
                project.Record.CountryId,
                CountryIso3 = project.Record.Country!.IsoCode,
                CountryName = project.Record.Country!.Name
            })
            .Select(group =>
            {
                var summary = FfcProjectBucketHelper.Summarize(group);
                var iso = (group.Key.CountryIso3 ?? string.Empty).ToUpperInvariant();
                var name = group.Key.CountryName ?? string.Empty;
                var latestYear = group.Max(project => project.Record.Year);

                return new FfcCountryRollupDto(
                    group.Key.CountryId,
                    iso,
                    name,
                    summary.Installed,
                    summary.DeliveredNotInstalled,
                    summary.Planned,
                    summary.Total,
                    latestYear);
            })
            .OrderByDescending(row => row.Total)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return aggregates;
    }
}
