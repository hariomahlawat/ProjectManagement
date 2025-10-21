using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests;

public class ProliferationOverviewServicePreferenceTests
{
    [Fact]
    public async Task GetPreferenceOverridesAsync_ReturnsOverridesAndAppliesFilters()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        context.Projects.AddRange(
            new Project
            {
                Id = 1,
                Name = "Project Alpha",
                CreatedByUserId = "creator",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                RowVersion = Guid.NewGuid().ToByteArray()
            },
            new Project
            {
                Id = 2,
                Name = "Project Beta",
                CreatedByUserId = "creator",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                RowVersion = Guid.NewGuid().ToByteArray()
            });

        var user1 = new ApplicationUser { Id = "user-1", FullName = "Alice Example", UserName = "alice" };
        var user2 = new ApplicationUser { Id = "user-2", UserName = "bob" };
        context.Users.AddRange(user1, user2);

        var preference1 = new ProliferationYearPreference
        {
            Id = Guid.NewGuid(),
            ProjectId = 1,
            Source = ProliferationSource.Sdd,
            Year = 2024,
            Mode = YearPreferenceMode.Auto,
            SetByUserId = user1.Id,
            SetOnUtc = new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        var preference2 = new ProliferationYearPreference
        {
            Id = Guid.NewGuid(),
            ProjectId = 2,
            Source = ProliferationSource.Sdd,
            Year = 2023,
            Mode = YearPreferenceMode.UseYearly,
            SetByUserId = user2.Id,
            SetOnUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var defaultPreference = new ProliferationYearPreference
        {
            Id = Guid.NewGuid(),
            ProjectId = 1,
            Source = ProliferationSource.Abw515,
            Year = 2024,
            Mode = YearPreferenceMode.UseYearlyAndGranular,
            SetByUserId = user1.Id,
            SetOnUtc = DateTime.UtcNow
        };

        context.ProliferationYearPreferences.AddRange(preference1, preference2, defaultPreference);

        var now = DateTime.UtcNow;
        context.ProliferationYearlies.AddRange(
            new ProliferationYearly
            {
                Id = Guid.NewGuid(),
                ProjectId = 1,
                Source = ProliferationSource.Sdd,
                Year = 2024,
                TotalQuantity = 12,
                ApprovalStatus = ApprovalStatus.Approved,
                SubmittedByUserId = "submitter-1",
                ApprovedByUserId = "approver",
                ApprovedOnUtc = now,
                CreatedOnUtc = now,
                LastUpdatedOnUtc = now,
                RowVersion = new byte[] { 1 }
            },
            new ProliferationYearly
            {
                Id = Guid.NewGuid(),
                ProjectId = 2,
                Source = ProliferationSource.Sdd,
                Year = 2023,
                TotalQuantity = 7,
                ApprovalStatus = ApprovalStatus.Approved,
                SubmittedByUserId = "submitter-2",
                ApprovedByUserId = "approver",
                ApprovedOnUtc = now,
                CreatedOnUtc = now,
                LastUpdatedOnUtc = now,
                RowVersion = new byte[] { 1 }
            });

        context.ProliferationGranularEntries.Add(new ProliferationGranular
        {
            Id = Guid.NewGuid(),
            ProjectId = 1,
            Source = ProliferationSource.Sdd,
            UnitName = "Unit",
            ProliferationDate = new DateOnly(2024, 4, 1),
            Quantity = 5,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "submitter-1",
            ApprovedByUserId = "approver",
            ApprovedOnUtc = now,
            CreatedOnUtc = now,
            LastUpdatedOnUtc = now,
            RowVersion = new byte[] { 1 }
        });

        await context.SaveChangesAsync();

        var readService = new ProliferationTrackerReadService(context);
        var service = new ProliferationOverviewService(context, readService);

        var allOverrides = await service.GetPreferenceOverridesAsync(
            new ProliferationPreferenceOverrideRequest(null, null, null, null),
            CancellationToken.None);

        Assert.Equal(2, allOverrides.Count);
        var autoOverride = Assert.Single(allOverrides.Where(x => x.ProjectId == 1));
        Assert.Equal(YearPreferenceMode.Auto, autoOverride.Mode);
        Assert.Equal(YearPreferenceMode.UseGranular, autoOverride.EffectiveMode);
        Assert.True(autoOverride.HasGranular);
        Assert.True(autoOverride.HasYearly);
        Assert.True(autoOverride.HasApprovedGranular);
        Assert.True(autoOverride.HasApprovedYearly);
        Assert.Equal(15, autoOverride.EffectiveTotal);
        Assert.Equal("Alice Example", autoOverride.SetByDisplayName);

        var filtered = await service.GetPreferenceOverridesAsync(
            new ProliferationPreferenceOverrideRequest(2, ProliferationSource.Sdd, 2023, "beta"),
            CancellationToken.None);
        var filteredRow = Assert.Single(filtered);
        Assert.Equal(2, filteredRow.ProjectId);
        Assert.Equal(YearPreferenceMode.UseYearly, filteredRow.EffectiveMode);
        Assert.False(filteredRow.HasGranular);
        Assert.False(filteredRow.HasApprovedGranular);

        var none = await service.GetPreferenceOverridesAsync(
            new ProliferationPreferenceOverrideRequest(null, null, null, "gamma"),
            CancellationToken.None);
        Assert.Empty(none);
    }
}
