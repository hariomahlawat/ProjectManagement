using System;
using System.Linq;
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
    public async Task GetAsync_ReturnsExpectedMetricsAndPreferenceMetadata()
    {
        await using var context = CreateContext();

        var unit = new SponsoringUnit { Id = 1, Name = "Strategy" };
        var simulator = new ApplicationUser { Id = "sim-1", FullName = "Wing Cmdr. Nia Hassan", UserName = "nia.hassan" };

        context.SponsoringUnits.Add(unit);
        context.Users.Add(simulator);

        var project = new Project
        {
            Id = 10,
            Name = "Project Atlas",
            SponsoringUnitId = unit.Id,
            SponsoringUnit = unit,
            LeadPoUserId = simulator.Id,
            LeadPoUser = simulator
        };

        context.Projects.Add(project);

        context.ProliferationYearlies.Add(new ProliferationYearly
        {
            ProjectId = project.Id,
            Project = project,
            Source = ProliferationSource.Internal,
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
            Source = ProliferationSource.Internal,
            Year = 2024,
            DirectBeneficiaries = 100,
            IndirectBeneficiaries = 320,
            InvestmentValue = 400.50m
        });

        context.ProliferationYearPreferences.Add(new ProliferationYearPreference
        {
            ProjectId = project.Id,
            UserId = "viewer-1",
            Source = ProliferationSource.Internal,
            Year = 2024,
            CreatedByUserId = "viewer-1",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync();

        var service = new ProliferationTrackerReadService(context);

        var rows = await service.GetAsync(new ProliferationTrackerFilter
        {
            UserId = "viewer-1",
            Source = ProliferationSource.Internal
        }, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(project.Id, row.ProjectId);
        Assert.Equal("Project Atlas", row.ProjectName);
        Assert.Equal(120, row.Yearly?.DirectBeneficiaries);
        Assert.Equal(100, row.GranularSum?.DirectBeneficiaries);
        Assert.Equal(20, row.Variance?.DirectBeneficiaries);
        Assert.Equal(450.75m, row.Yearly?.InvestmentValue);
        Assert.Equal(400.50m, row.GranularSum?.InvestmentValue);
        Assert.Equal(50.25m, row.Variance?.InvestmentValue);
        Assert.Equal(row.Yearly?.DirectBeneficiaries, row.Effective?.DirectBeneficiaries);
        Assert.Equal(ProliferationPreferenceMode.UseYearly, row.Preference.Mode);
        Assert.True(row.Preference.HasPreference);
        Assert.True(row.Preference.PreferredYearMatches);
        Assert.Equal(2024, row.Preference.PreferredYear);
        Assert.NotNull(row.Preference.RowVersion);
        Assert.Equal("Wing Cmdr. Nia Hassan", row.SimulatorDisplayName);
    }

    [Fact]
    public async Task GetAsync_FallsBackToGranularWhenYearlyMissing()
    {
        await using var context = CreateContext();

        var unit = new SponsoringUnit { Id = 2, Name = "Operations" };
        var simulator = new ApplicationUser { Id = "sim-2", FullName = "Sqn Ldr. Leo Park", UserName = "leo.park" };
        context.SponsoringUnits.Add(unit);
        context.Users.Add(simulator);

        var project = new Project
        {
            Id = 11,
            Name = "Project Horizon",
            SponsoringUnitId = unit.Id,
            SponsoringUnit = unit,
            LeadPoUserId = simulator.Id,
            LeadPoUser = simulator
        };

        context.Projects.Add(project);

        context.ProliferationGranularYearlyView.Add(new ProliferationGranularYearly
        {
            ProjectId = project.Id,
            Source = ProliferationSource.External,
            Year = 2025,
            DirectBeneficiaries = 75,
            IndirectBeneficiaries = 190,
            InvestmentValue = 260.00m
        });

        context.ProliferationYearPreferences.Add(new ProliferationYearPreference
        {
            ProjectId = project.Id,
            UserId = "viewer-2",
            Source = ProliferationSource.External,
            Year = 2025,
            CreatedByUserId = "viewer-2",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync();

        var service = new ProliferationTrackerReadService(context);

        var rows = await service.GetAsync(new ProliferationTrackerFilter
        {
            UserId = "viewer-2",
            Source = ProliferationSource.External
        }, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Null(row.Yearly);
        Assert.NotNull(row.GranularSum);
        Assert.Equal(75, row.Effective?.DirectBeneficiaries);
        Assert.Equal(ProliferationPreferenceMode.UseGranular, row.Preference.Mode);
        Assert.True(row.Preference.HasPreference);
        Assert.True(row.Preference.PreferredYearMatches);
        Assert.Equal(2025, row.Preference.PreferredYear);
        Assert.NotNull(row.Preference.RowVersion);
        Assert.Null(row.Variance);
    }

    [Fact]
    public async Task GetAsync_FilteringByUnitAndSimulatorLimitsResults()
    {
        await using var context = CreateContext();

        var unitA = new SponsoringUnit { Id = 3, Name = "Innovation" };
        var unitB = new SponsoringUnit { Id = 4, Name = "Support" };
        var simulatorA = new ApplicationUser { Id = "sim-3", FullName = "Gp Capt. Mira Rao", UserName = "mira.rao" };
        var simulatorB = new ApplicationUser { Id = "sim-4", FullName = "Gp Capt. Arun Das", UserName = "arun.das" };

        context.SponsoringUnits.AddRange(unitA, unitB);
        context.Users.AddRange(simulatorA, simulatorB);

        var projectA = new Project
        {
            Id = 21,
            Name = "Project Nova",
            SponsoringUnitId = unitA.Id,
            SponsoringUnit = unitA,
            LeadPoUserId = simulatorA.Id,
            LeadPoUser = simulatorA
        };

        var projectB = new Project
        {
            Id = 22,
            Name = "Project Lyra",
            SponsoringUnitId = unitB.Id,
            SponsoringUnit = unitB,
            LeadPoUserId = simulatorB.Id,
            LeadPoUser = simulatorB
        };

        context.Projects.AddRange(projectA, projectB);

        context.ProliferationYearlies.AddRange(
            new ProliferationYearly
            {
                ProjectId = projectA.Id,
                Project = projectA,
                Source = ProliferationSource.Internal,
                Year = 2023,
                Metrics = new ProliferationMetrics { DirectBeneficiaries = 40 },
                CreatedByUserId = "system",
                CreatedAtUtc = DateTimeOffset.UtcNow
            },
            new ProliferationYearly
            {
                ProjectId = projectB.Id,
                Project = projectB,
                Source = ProliferationSource.Internal,
                Year = 2023,
                Metrics = new ProliferationMetrics { DirectBeneficiaries = 55 },
                CreatedByUserId = "system",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

        await context.SaveChangesAsync();

        var service = new ProliferationTrackerReadService(context);

        var rows = await service.GetAsync(new ProliferationTrackerFilter
        {
            SponsoringUnitId = unitA.Id,
            SimulatorUserId = simulatorA.Id,
            Source = ProliferationSource.Internal
        }, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(projectA.Id, row.ProjectId);
        Assert.Equal("Project Nova", row.ProjectName);
        Assert.Equal(unitA.Id, row.SponsoringUnitId);
        Assert.Equal(simulatorA.Id, row.SimulatorUserId);
    }

    [Fact]
    public async Task GetAsync_ReportsPreferenceForDifferentYear()
    {
        await using var context = CreateContext();

        var project = new Project { Id = 31, Name = "Project Helios" };
        context.Projects.Add(project);

        context.ProliferationYearlies.AddRange(
            new ProliferationYearly
            {
                ProjectId = project.Id,
                Project = project,
                Source = ProliferationSource.Internal,
                Year = 2023,
                Metrics = new ProliferationMetrics { DirectBeneficiaries = 30 },
                CreatedByUserId = "analyst",
                CreatedAtUtc = DateTimeOffset.UtcNow
            },
            new ProliferationYearly
            {
                ProjectId = project.Id,
                Project = project,
                Source = ProliferationSource.Internal,
                Year = 2024,
                Metrics = new ProliferationMetrics { DirectBeneficiaries = 35 },
                CreatedByUserId = "analyst",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

        context.ProliferationYearPreferences.Add(new ProliferationYearPreference
        {
            ProjectId = project.Id,
            UserId = "viewer-3",
            Source = ProliferationSource.Internal,
            Year = 2023,
            CreatedByUserId = "viewer-3",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync();

        var service = new ProliferationTrackerReadService(context);

        var rows = await service.GetAsync(new ProliferationTrackerFilter
        {
            UserId = "viewer-3",
            Source = ProliferationSource.Internal
        }, CancellationToken.None);

        Assert.Equal(2, rows.Count);

        var preferredRow = Assert.Single(rows.Where(r => r.Year == 2023));
        Assert.Equal(ProliferationPreferenceMode.UseYearly, preferredRow.Preference.Mode);
        Assert.True(preferredRow.Preference.HasPreference);
        Assert.True(preferredRow.Preference.PreferredYearMatches);

        var otherRow = Assert.Single(rows.Where(r => r.Year == 2024));
        Assert.Equal(ProliferationPreferenceMode.Auto, otherRow.Preference.Mode);
        Assert.True(otherRow.Preference.HasPreference);
        Assert.False(otherRow.Preference.PreferredYearMatches);
        Assert.Equal(2023, otherRow.Preference.PreferredYear);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
