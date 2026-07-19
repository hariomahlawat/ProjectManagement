using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services.Ffc;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcPortfolioServiceTests
{
    [Fact]
    public async Task GetSummaryAsync_ReturnsFilteredProjectUnitTotals()
    {
        await using var db = CreateDbContext();
        var alpha = new FfcCountry { Name = "Alpha", IsoCode = "ALP" };
        var beta = new FfcCountry { Name = "Beta", IsoCode = "BET" };
        db.FfcCountries.AddRange(alpha, beta);
        await db.SaveChangesAsync();

        var alphaRecord = new FfcRecord { CountryId = alpha.Id, Year = 2026 };
        var betaRecord = new FfcRecord { CountryId = beta.Id, Year = 2026 };
        db.FfcRecords.AddRange(alphaRecord, betaRecord);
        await db.SaveChangesAsync();

        db.FfcProjects.AddRange(
            new FfcProject
            {
                FfcRecordId = alphaRecord.Id,
                Name = "Installed",
                Quantity = 2,
                IsDelivered = true,
                IsInstalled = true,
                LinkedProjectId = 11
            },
            new FfcProject
            {
                FfcRecordId = alphaRecord.Id,
                Name = "Delivered",
                Quantity = 3,
                IsDelivered = true
            },
            new FfcProject
            {
                FfcRecordId = alphaRecord.Id,
                Name = "Planned",
                Quantity = 5
            },
            new FfcProject
            {
                FfcRecordId = betaRecord.Id,
                Name = "Other",
                Quantity = 7
            });
        await db.SaveChangesAsync();

        var service = new FfcPortfolioService(db);
        var summary = await service.GetSummaryAsync(new FfcPortfolioFilter(CountryId: alpha.Id));

        Assert.Equal(1, summary.RecordCount);
        Assert.Equal(1, summary.CountryCount);
        Assert.Equal(3, summary.ProjectCount);
        Assert.Equal(1, summary.LinkedProjectCount);
        Assert.Equal(2, summary.InstalledUnits);
        Assert.Equal(3, summary.DeliveredNotInstalledUnits);
        Assert.Equal(5, summary.PlannedUnits);
        Assert.Equal(10, summary.TotalUnits);
        Assert.Equal(50, summary.DeliveryPercent);
        Assert.Equal(20, summary.InstallationPercent);
    }

    [Theory]
    [InlineData(0, 0, FfcCompletionState.Pending)]
    [InlineData(0, 5, FfcCompletionState.Pending)]
    [InlineData(2, 5, FfcCompletionState.Partial)]
    [InlineData(5, 5, FfcCompletionState.Complete)]
    public void CompletionStateResolvers_ReturnExpectedState(
        int completed,
        int total,
        FfcCompletionState expected)
    {
        var summary = new FfcProjectQuantitySummary(
            Installed: completed,
            DeliveredNotInstalled: 0,
            Planned: Math.Max(0, total - completed));

        Assert.Equal(expected, FfcPortfolioQuery.ResolveInstallationState(summary));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
