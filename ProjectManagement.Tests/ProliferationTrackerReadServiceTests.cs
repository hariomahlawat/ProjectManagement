using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProliferationTrackerReadServiceTests
{
    [Fact]
    public async Task GetAsync_WithAutoModePrefersYearlyMetricsWhenAvailable()
    {
        await using var context = CreateContext();

        var unit = new SponsoringUnit { Id = 1, Name = "Strategy" };
        var simulator = new ApplicationUser { Id = "sim-1", FullName = "Wing Cmdr. Mina Iyer" };
        var project = new Project
        {
            Id = 100,
            Name = "Project Meridian",
            SponsoringUnitId = unit.Id,
            SponsoringUnit = unit,
            LeadPoUserId = simulator.Id,
            LeadPoUser = simulator
        };

        context.SponsoringUnits.Add(unit);
        context.Users.Add(simulator);
        context.Projects.Add(project);

        context.ProliferationYearlies.Add(new ProliferationYearly
        {
            ProjectId = project.Id,
            Project = project,
            Source = ProliferationSource.Sdd,
            Year = 2024,
            Metrics = new ProliferationMetrics
            {
                DirectBeneficiaries = 120,
                IndirectBeneficiaries = 340,
                InvestmentValue = 450.75m
            },
            CreatedByUserId = "reporter",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        context.ProliferationGranularYearlyView.Add(new ProliferationGranularYearly
        {
            ProjectId = project.Id,
            Source = ProliferationSource.Sdd,
            Year = 2024,
            DirectBeneficiaries = 110,
            IndirectBeneficiaries = 360,
            InvestmentValue = 430.50m
        });

        await context.SaveChangesAsync();

        var service = new ProliferationTrackerReadService(context);
        var rows = await service.GetAsync(new ProliferationTrackerFilter
        {
            UserId = "viewer-1",
            Source = ProliferationSource.Sdd
        }, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(project.Id, row.ProjectId);
        Assert.Equal(ProliferationPreferenceMode.Auto, row.Preference.Mode);
        Assert.False(row.Preference.HasPreference);
        Assert.Equal(120, row.Yearly?.DirectBeneficiaries);
        Assert.Equal(110, row.GranularSum?.DirectBeneficiaries);
        Assert.Equal(120, row.Effective?.DirectBeneficiaries);
        Assert.Equal(450.75m, row.Effective?.InvestmentValue);
    }

    [Fact]
    public async Task GetAsync_WithAutoModeFallsBackToGranularWhenYearlyMissing()
    {
        await using var context = CreateContext();

        var unit = new SponsoringUnit { Id = 2, Name = "Operations" };
        var simulator = new ApplicationUser { Id = "sim-2", FullName = "Sqn Ldr. Omar Singh" };
        var project = new Project
        {
            Id = 101,
            Name = "Project Horizon",
            SponsoringUnitId = unit.Id,
            SponsoringUnit = unit,
            LeadPoUserId = simulator.Id,
            LeadPoUser = simulator
        };

        context.SponsoringUnits.Add(unit);
        context.Users.Add(simulator);
        context.Projects.Add(project);

        context.ProliferationGranularYearlyView.Add(new ProliferationGranularYearly
        {
            ProjectId = project.Id,
            Source = ProliferationSource.Abw515,
            Year = 2025,
            DirectBeneficiaries = 95,
            IndirectBeneficiaries = 210,
            InvestmentValue = 265.25m
        });

        await context.SaveChangesAsync();

        var service = new ProliferationTrackerReadService(context);
        var rows = await service.GetAsync(new ProliferationTrackerFilter
        {
            UserId = "viewer-2",
            Source = ProliferationSource.Abw515
        }, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(ProliferationPreferenceMode.Auto, row.Preference.Mode);
        Assert.False(row.Preference.HasPreference);
        Assert.Null(row.Yearly);
        Assert.NotNull(row.GranularSum);
        Assert.Equal(95, row.Effective?.DirectBeneficiaries);
        Assert.Equal(210, row.Effective?.IndirectBeneficiaries);
        Assert.Equal(265.25m, row.Effective?.InvestmentValue);
    }

    [Fact]
    public async Task GetAsync_WithUseYearlyPreferenceHonorsYearlyTotals()
    {
        await using var context = CreateContext();

        var unit = new SponsoringUnit { Id = 3, Name = "Innovation" };
        var simulator = new ApplicationUser { Id = "sim-3", FullName = "Gp Capt. Priya Arora" };
        var project = new Project
        {
            Id = 102,
            Name = "Project Aurora",
            SponsoringUnitId = unit.Id,
            SponsoringUnit = unit,
            LeadPoUserId = simulator.Id,
            LeadPoUser = simulator
        };

        context.SponsoringUnits.Add(unit);
        context.Users.Add(simulator);
        context.Projects.Add(project);

        context.ProliferationYearlies.Add(new ProliferationYearly
        {
            ProjectId = project.Id,
            Project = project,
            Source = ProliferationSource.Sdd,
            Year = 2023,
            Metrics = new ProliferationMetrics
            {
                DirectBeneficiaries = 80,
                IndirectBeneficiaries = 150,
                InvestmentValue = 300.00m
            },
            CreatedByUserId = "analyst",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        context.ProliferationGranularYearlyView.Add(new ProliferationGranularYearly
        {
            ProjectId = project.Id,
            Source = ProliferationSource.Sdd,
            Year = 2023,
            DirectBeneficiaries = 70,
            IndirectBeneficiaries = 140,
            InvestmentValue = 280.00m
        });

        context.ProliferationYearPreferences.Add(new ProliferationYearPreference
        {
            ProjectId = project.Id,
            Source = ProliferationSource.Sdd,
            UserId = "viewer-3",
            Year = 2023,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "viewer-3",
            LastModifiedAtUtc = DateTimeOffset.UtcNow,
            LastModifiedByUserId = "viewer-3",
            RowVersion = Guid.NewGuid().ToByteArray()
        });

        await context.SaveChangesAsync();

        var service = new ProliferationTrackerReadService(context);
        var rows = await service.GetAsync(new ProliferationTrackerFilter
        {
            UserId = "viewer-3",
            Source = ProliferationSource.Sdd
        }, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(ProliferationPreferenceMode.UseYearly, row.Preference.Mode);
        Assert.True(row.Preference.HasPreference);
        Assert.True(row.Preference.PreferredYearMatches);
        Assert.Equal(80, row.Effective?.DirectBeneficiaries);
        Assert.Equal(150, row.Effective?.IndirectBeneficiaries);
        Assert.Equal(300.00m, row.Effective?.InvestmentValue);
    }

    [Fact]
    public async Task GetAsync_WithUseGranularPreferenceFallsBackWhenYearlyUnavailable()
    {
        await using var context = CreateContext();

        var unit = new SponsoringUnit { Id = 4, Name = "Support" };
        var simulator = new ApplicationUser { Id = "sim-4", FullName = "Gp Capt. Laura Chen" };
        var project = new Project
        {
            Id = 103,
            Name = "Project Solstice",
            SponsoringUnitId = unit.Id,
            SponsoringUnit = unit,
            LeadPoUserId = simulator.Id,
            LeadPoUser = simulator
        };

        context.SponsoringUnits.Add(unit);
        context.Users.Add(simulator);
        context.Projects.Add(project);

        context.ProliferationGranularYearlyView.Add(new ProliferationGranularYearly
        {
            ProjectId = project.Id,
            Source = ProliferationSource.Abw515,
            Year = 2026,
            DirectBeneficiaries = 60,
            IndirectBeneficiaries = 125,
            InvestmentValue = 190.00m
        });

        context.ProliferationYearPreferences.Add(new ProliferationYearPreference
        {
            ProjectId = project.Id,
            Source = ProliferationSource.Abw515,
            UserId = "viewer-4",
            Year = 2026,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "viewer-4",
            LastModifiedAtUtc = DateTimeOffset.UtcNow,
            LastModifiedByUserId = "viewer-4",
            RowVersion = Guid.NewGuid().ToByteArray()
        });

        await context.SaveChangesAsync();

        var service = new ProliferationTrackerReadService(context);
        var rows = await service.GetAsync(new ProliferationTrackerFilter
        {
            UserId = "viewer-4",
            Source = ProliferationSource.Abw515
        }, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(ProliferationPreferenceMode.UseGranular, row.Preference.Mode);
        Assert.True(row.Preference.HasPreference);
        Assert.True(row.Preference.PreferredYearMatches);
        Assert.Null(row.Yearly);
        Assert.NotNull(row.GranularSum);
        Assert.Equal(60, row.Effective?.DirectBeneficiaries);
        Assert.Equal(125, row.Effective?.IndirectBeneficiaries);
        Assert.Equal(190.00m, row.Effective?.InvestmentValue);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
