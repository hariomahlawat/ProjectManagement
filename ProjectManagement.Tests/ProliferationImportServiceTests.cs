using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProliferationImportServiceTests
{
    [Fact]
    public async Task YearlyImport_ImportsValidRowsAndReturnsRejectionFile()
    {
        await using var context = CreateContext();
        context.Projects.Add(new Project
        {
            Id = 10,
            Name = "Project Atlas",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            CreatedByUserId = "seed"
        });
        await context.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new ProliferationYearlyImportService(context, clock, NullLogger<ProliferationYearlyImportService>.Instance);

        var csv = new StringBuilder();
        csv.AppendLine("ProjectId,Year,DirectBeneficiaries,IndirectBeneficiaries,InvestmentValue,Notes");
        csv.AppendLine("10,2024,100,200,123.45,Primary record");
        csv.AppendLine("99,2024,abc,100,50,Invalid row");

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv.ToString()));
        var request = new ProliferationYearlyImportRequest(stream, "yearly.csv", ProliferationSource.Internal, "tester");

        var result = await service.ImportAsync(request, CancellationToken.None);

        Assert.Equal(2, result.ProcessedRows);
        Assert.Equal(1, result.ImportedRows);
        Assert.True(result.HasErrors);
        Assert.NotNull(result.RejectionFile);
        Assert.NotEmpty(result.Errors);

        var stored = await context.ProliferationYearlies.SingleAsync();
        Assert.Equal(10, stored.ProjectId);
        Assert.Equal(2024, stored.Year);
        Assert.Equal(100, stored.Metrics.DirectBeneficiaries);
        Assert.Equal("tester", stored.CreatedByUserId);
    }

    [Fact]
    public async Task GranularImport_ImportsMonthlyRecords()
    {
        await using var context = CreateContext();
        context.Projects.Add(new Project
        {
            Id = 5,
            Name = "Project Horizon",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            CreatedByUserId = "seed"
        });
        await context.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new ProliferationGranularImportService(context, clock, NullLogger<ProliferationGranularImportService>.Instance);

        var csv = new StringBuilder();
        csv.AppendLine("ProjectId,Year,Granularity,Period,DirectBeneficiaries,IndirectBeneficiaries,InvestmentValue,PeriodLabel");
        csv.AppendLine("5,2024,Monthly,1,25,40,10.5,Jan");
        csv.AppendLine("5,2024,Monthly,15,30,50,20,Invalid period");

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv.ToString()));
        var request = new ProliferationGranularImportRequest(stream, "granular.csv", ProliferationSource.Internal, "uploader");

        var result = await service.ImportAsync(request, CancellationToken.None);

        Assert.Equal(2, result.ProcessedRows);
        Assert.Equal(1, result.ImportedRows);
        Assert.True(result.HasErrors);
        Assert.NotNull(result.RejectionFile);

        var stored = await context.ProliferationGranularEntries.SingleAsync();
        Assert.Equal(5, stored.ProjectId);
        Assert.Equal(1, stored.Period);
        Assert.Equal(ProliferationGranularity.Monthly, stored.Granularity);
        Assert.Equal(25, stored.Metrics.DirectBeneficiaries);
        Assert.Equal("uploader", stored.CreatedByUserId);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }
}
