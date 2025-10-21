using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using Xunit;

namespace ProjectManagement.Tests;

public class ProliferationTrackerReadServiceTests
{
    [Fact]
    public async Task GetEffectiveTotalAsync_DefaultsToCombinedTotals()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var yearlyId = Guid.NewGuid();
        context.ProliferationYearlies.Add(new ProliferationYearly
        {
            Id = yearlyId,
            ProjectId = 1,
            Source = ProliferationSource.Sdd,
            Year = 2024,
            TotalQuantity = 10,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "user-1",
            CreatedOnUtc = DateTime.UtcNow,
            LastUpdatedOnUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        });

        context.ProliferationGranularEntries.Add(new ProliferationGranular
        {
            Id = Guid.NewGuid(),
            ProjectId = 1,
            Source = ProliferationSource.Sdd,
            UnitName = "Unit",
            ProliferationDate = new DateOnly(2024, 1, 1),
            Quantity = 20,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "user-1",
            CreatedOnUtc = DateTime.UtcNow,
            LastUpdatedOnUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        });

        await context.SaveChangesAsync();

        var service = new ProliferationTrackerReadService(context);
        var combinedTotal = await service.GetEffectiveTotalAsync(1, ProliferationSource.Sdd, 2024, CancellationToken.None);
        Assert.Equal(30, combinedTotal);

        context.ProliferationGranularEntries.RemoveRange(context.ProliferationGranularEntries);
        await context.SaveChangesAsync();

        var yearlyTotal = await service.GetEffectiveTotalAsync(1, ProliferationSource.Sdd, 2024, CancellationToken.None);
        Assert.Equal(10, yearlyTotal);
    }

    [Fact]
    public async Task GetEffectiveTotalAsync_RespectsYearPreference()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        context.ProliferationYearlies.Add(new ProliferationYearly
        {
            Id = Guid.NewGuid(),
            ProjectId = 2,
            Source = ProliferationSource.Sdd,
            Year = 2025,
            TotalQuantity = 50,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "user-2",
            CreatedOnUtc = DateTime.UtcNow,
            LastUpdatedOnUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        });

        context.ProliferationGranularEntries.Add(new ProliferationGranular
        {
            Id = Guid.NewGuid(),
            ProjectId = 2,
            Source = ProliferationSource.Sdd,
            UnitName = "Unit",
            ProliferationDate = new DateOnly(2025, 1, 1),
            Quantity = 80,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "user-2",
            CreatedOnUtc = DateTime.UtcNow,
            LastUpdatedOnUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        });

        context.ProliferationYearPreferences.Add(new ProliferationYearPreference
        {
            Id = Guid.NewGuid(),
            ProjectId = 2,
            Source = ProliferationSource.Sdd,
            Year = 2025,
            Mode = YearPreferenceMode.UseYearly,
            SetByUserId = "pref-user",
            SetOnUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        var service = new ProliferationTrackerReadService(context);

        var yearlyPreferred = await service.GetEffectiveTotalAsync(2, ProliferationSource.Sdd, 2025, CancellationToken.None);
        Assert.Equal(50, yearlyPreferred);

        context.ProliferationYearPreferences.RemoveRange(context.ProliferationYearPreferences);
        context.ProliferationYearPreferences.Add(new ProliferationYearPreference
        {
            Id = Guid.NewGuid(),
            ProjectId = 2,
            Source = ProliferationSource.Sdd,
            Year = 2025,
            Mode = YearPreferenceMode.UseGranular,
            SetByUserId = "pref-user",
            SetOnUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        var granularPreferred = await service.GetEffectiveTotalAsync(2, ProliferationSource.Sdd, 2025, CancellationToken.None);
        Assert.Equal(80, granularPreferred);

        context.ProliferationYearPreferences.RemoveRange(context.ProliferationYearPreferences);
        context.ProliferationYearPreferences.Add(new ProliferationYearPreference
        {
            Id = Guid.NewGuid(),
            ProjectId = 2,
            Source = ProliferationSource.Sdd,
            Year = 2025,
            Mode = YearPreferenceMode.UseYearlyAndGranular,
            SetByUserId = "pref-user",
            SetOnUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        var combinedPreferred = await service.GetEffectiveTotalAsync(2, ProliferationSource.Sdd, 2025, CancellationToken.None);
        Assert.Equal(130, combinedPreferred);
    }

    [Fact]
    public async Task GetEffectiveTotalAsync_HonorsAutoPreference()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        context.ProliferationYearlies.Add(new ProliferationYearly
        {
            Id = Guid.NewGuid(),
            ProjectId = 9,
            Source = ProliferationSource.Sdd,
            Year = 2027,
            TotalQuantity = 40,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "user-9",
            CreatedOnUtc = DateTime.UtcNow,
            LastUpdatedOnUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        });

        context.ProliferationGranularEntries.Add(new ProliferationGranular
        {
            Id = Guid.NewGuid(),
            ProjectId = 9,
            Source = ProliferationSource.Sdd,
            UnitName = "Unit",
            ProliferationDate = new DateOnly(2027, 5, 1),
            Quantity = 15,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "user-9",
            CreatedOnUtc = DateTime.UtcNow,
            LastUpdatedOnUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        });

        context.ProliferationYearPreferences.Add(new ProliferationYearPreference
        {
            Id = Guid.NewGuid(),
            ProjectId = 9,
            Source = ProliferationSource.Sdd,
            Year = 2027,
            Mode = YearPreferenceMode.Auto,
            SetByUserId = "pref",
            SetOnUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        var service = new ProliferationTrackerReadService(context);
        var result = await service.GetEffectiveTotalAsync(9, ProliferationSource.Sdd, 2027, CancellationToken.None);

        Assert.Equal(15, result);
    }

    [Fact]
    public async Task GetEffectiveTotalAsync_UsesYearlyForAbw515()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        context.ProliferationYearlies.Add(new ProliferationYearly
        {
            Id = Guid.NewGuid(),
            ProjectId = 3,
            Source = ProliferationSource.Abw515,
            Year = 2026,
            TotalQuantity = 35,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "user-3",
            CreatedOnUtc = DateTime.UtcNow,
            LastUpdatedOnUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        });

        context.ProliferationGranularEntries.Add(new ProliferationGranular
        {
            Id = Guid.NewGuid(),
            ProjectId = 3,
            Source = ProliferationSource.Abw515,
            UnitName = "Unit",
            ProliferationDate = new DateOnly(2026, 1, 1),
            Quantity = 90,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "user-3",
            CreatedOnUtc = DateTime.UtcNow,
            LastUpdatedOnUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        });

        await context.SaveChangesAsync();

        var service = new ProliferationTrackerReadService(context);
        var result = await service.GetEffectiveTotalAsync(3, ProliferationSource.Abw515, 2026, CancellationToken.None);

        Assert.Equal(35, result);
    }
}
