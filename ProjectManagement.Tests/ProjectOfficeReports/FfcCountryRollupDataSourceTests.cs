using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;
using ProjectManagement.Data;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcCountryRollupDataSourceTests
{
    // SECTION: Aggregation test harness
    [Fact]
    public async Task LoadAsync_WithMixedProjects_ReturnsCountryTotals()
    {
        await using var db = CreateDbContext();
        var (alpha, beta, gamma) = await SeedCountriesAsync(db);
        var (alphaRecord, betaRecord, gammaRecord) = await SeedRecordsAsync(db, alpha, beta, gamma);

        db.FfcProjects.AddRange(
            new FfcProject
            {
                FfcRecordId = alphaRecord.Id,
                Name = "Alpha Install",
                Quantity = 3,
                IsDelivered = true,
                IsInstalled = true
            },
            new FfcProject
            {
                FfcRecordId = alphaRecord.Id,
                Name = "Alpha Delivery",
                Quantity = 2,
                IsDelivered = true
            },
            new FfcProject
            {
                FfcRecordId = betaRecord.Id,
                Name = "Beta Planned",
                Quantity = 5
            },
            new FfcProject
            {
                FfcRecordId = gammaRecord.Id,
                Name = "Gamma Ignore",
                Quantity = 10,
                IsDelivered = true,
                IsInstalled = true
            });
        await db.SaveChangesAsync();

        var result = await FfcCountryRollupDataSource.LoadAsync(db, CancellationToken.None);

        Assert.Equal(2, result.Count);

        var alphaRow = Assert.Single(result.Where(row => row.CountryId == alpha.Id));
        Assert.Equal(3, alphaRow.Installed);
        Assert.Equal(2, alphaRow.Delivered);
        Assert.Equal(0, alphaRow.Planned);
        Assert.Equal(5, alphaRow.Total);

        var betaRow = Assert.Single(result.Where(row => row.CountryId == beta.Id));
        Assert.Equal(0, betaRow.Installed);
        Assert.Equal(0, betaRow.Delivered);
        Assert.Equal(5, betaRow.Planned);
        Assert.Equal(5, betaRow.Total);
    }

    private static async Task<(FfcCountry Alpha, FfcCountry Beta, FfcCountry Gamma)> SeedCountriesAsync(ApplicationDbContext db)
    {
        var alpha = new FfcCountry { Name = "Alpha", IsoCode = "ALP", IsActive = true };
        var beta = new FfcCountry { Name = "Beta", IsoCode = "BET", IsActive = true };
        var gamma = new FfcCountry { Name = "Gamma", IsoCode = "GAM", IsActive = false };
        db.FfcCountries.AddRange(alpha, beta, gamma);
        await db.SaveChangesAsync();
        return (alpha, beta, gamma);
    }

    private static async Task<(FfcRecord Alpha, FfcRecord Beta, FfcRecord Gamma)> SeedRecordsAsync(
        ApplicationDbContext db,
        FfcCountry alpha,
        FfcCountry beta,
        FfcCountry gamma)
    {
        var alphaRecord = new FfcRecord { CountryId = alpha.Id, Year = (short)DateTime.UtcNow.Year };
        var betaRecord = new FfcRecord { CountryId = beta.Id, Year = (short)DateTime.UtcNow.Year };
        var gammaRecord = new FfcRecord { CountryId = gamma.Id, Year = (short)DateTime.UtcNow.Year };
        db.FfcRecords.AddRange(alphaRecord, betaRecord, gammaRecord);
        await db.SaveChangesAsync();
        return (alphaRecord, betaRecord, gammaRecord);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
