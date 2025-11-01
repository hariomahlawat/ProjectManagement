using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

public sealed record FfcCountryRollupDto(
    long CountryId,
    string Iso3,
    string Name,
    int Installed,
    int Delivered,
    int Planned,
    int Total
);

internal static class FfcCountryRollupDataSource
{
    public static async Task<IReadOnlyList<FfcCountryRollupDto>> LoadAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var linkedProjects = await db.FfcProjects
            .AsNoTracking()
            .Where(project => project.LinkedProjectId != null)
            .Where(project =>
                !project.Record.IsDeleted &&
                project.Record.Country.IsActive &&
                project.LinkedProject != null &&
                !project.LinkedProject.IsDeleted &&
                project.LinkedProject.LifecycleStatus == ProjectLifecycleStatus.Completed)
            .Select(project => new
            {
                ProjectId = project.LinkedProjectId!.Value,
                project.Record.CountryId,
                CountryIso3 = project.Record.Country.IsoCode,
                CountryName = project.Record.Country.Name,
                project.Record.InstallationYes,
                project.Record.DeliveryYes
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        var aggregates = linkedProjects
            .GroupBy(item => new { item.CountryId, item.CountryIso3, item.CountryName })
            .Select(group =>
            {
                var installed = group.Count(record => record.InstallationYes);
                var delivered = group.Count(record => !record.InstallationYes && record.DeliveryYes);
                var planned = group.Count(record => !record.InstallationYes && !record.DeliveryYes);
                var iso = (group.Key.CountryIso3 ?? string.Empty).ToUpperInvariant();
                var name = group.Key.CountryName ?? string.Empty;

                return new FfcCountryRollupDto(
                    group.Key.CountryId,
                    iso,
                    name,
                    installed,
                    delivered,
                    planned,
                    installed + delivered + planned);
            })
            .OrderByDescending(row => row.Total)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return aggregates;
    }
}
