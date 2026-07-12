using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ProjectManagement.Data;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class SocialMediaMigrationsTests
{
    [Fact]
    public void CurrentModel_DefinesRequiredSocialMediaDefaults()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"social-media-model-{Guid.NewGuid():N}")
            .Options;

        using var context = new ApplicationDbContext(options);
        var entityType = context.Model.FindEntityType(typeof(SocialMediaEventType));
        Assert.NotNull(entityType);

        var seedRows = entityType!.GetSeedData()
            .Select(row => row.ToDictionary(entry => entry.Key, entry => entry.Value))
            .ToArray();

        Assert.Contains(seedRows, row =>
            Equals(row["Name"], "Campaign Launch")
            && Equals(row["CreatedByUserId"], "system")
            && Equals(row["IsActive"], true));
        Assert.Contains(seedRows, row =>
            Equals(row["Name"], "Milestone Update")
            && Equals(row["IsActive"], true));
        Assert.Contains(seedRows, row =>
            Equals(row["Name"], "Community Engagement")
            && Equals(row["IsActive"], true));
    }
}
