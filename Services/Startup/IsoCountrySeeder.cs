using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Startup;

public sealed class IsoCountrySeeder
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<IsoCountrySeeder> _logger;

    public IsoCountrySeeder(ApplicationDbContext db, ILogger<IsoCountrySeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IsoCountrySeedData.IsoCountrySeedRow> rows;
        try
        {
            rows = IsoCountrySeedData.Load();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to load ISO-3166 country seed data.");
            return;
        }

        if (rows.Count == 0)
        {
            _logger.LogWarning("ISO-3166 seed data file was empty; no countries were seeded.");
            return;
        }

        var countries = await _db.FfcCountries.ToListAsync(cancellationToken);
        var isoLookup = countries
            .Where(c => !string.IsNullOrWhiteSpace(c.IsoCode))
            .ToDictionary(c => c.IsoCode.Trim().ToUpperInvariant(), c => c, StringComparer.OrdinalIgnoreCase);

        var nameLookup = countries
            .ToDictionary(c => NormalizeName(c.Name), c => c, StringComparer.OrdinalIgnoreCase);

        var inserted = 0;
        var updated = 0;

        foreach (var row in rows)
        {
            var iso3 = NormalizeIso(row.Alpha3);
            var name = row.Name?.Trim();

            if (string.IsNullOrWhiteSpace(iso3) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (isoLookup.TryGetValue(iso3, out var existingByIso))
            {
                if (!string.Equals(existingByIso.Name, name, StringComparison.Ordinal))
                {
                    existingByIso.Name = name;
                    updated++;
                }

                continue;
            }

            var normalizedName = NormalizeName(name);
            if (nameLookup.TryGetValue(normalizedName, out var existingByName))
            {
                var changed = false;
                if (!string.Equals(existingByName.IsoCode, iso3, StringComparison.OrdinalIgnoreCase))
                {
                    existingByName.IsoCode = iso3;
                    changed = true;
                }

                if (!string.Equals(existingByName.Name, name, StringComparison.Ordinal))
                {
                    existingByName.Name = name;
                    changed = true;
                }

                if (changed)
                {
                    updated++;
                }

                isoLookup[iso3] = existingByName;
                continue;
            }

            var entity = new FfcCountry
            {
                Name = name,
                IsoCode = iso3,
                IsActive = true
            };

            _db.FfcCountries.Add(entity);
            isoLookup[iso3] = entity;
            nameLookup[normalizedName] = entity;
            inserted++;
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "ISO-3166 countries seeded. Inserted {Inserted} and updated {Updated} entries.",
                inserted,
                updated);
        }
        else
        {
            _logger.LogInformation("ISO-3166 countries are already up to date.");
        }
    }

    private static string NormalizeIso(string? iso)
        => string.IsNullOrWhiteSpace(iso) ? string.Empty : iso.Trim().ToUpperInvariant();

    private static string NormalizeName(string? name)
        => string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim().ToUpperInvariant();
}
