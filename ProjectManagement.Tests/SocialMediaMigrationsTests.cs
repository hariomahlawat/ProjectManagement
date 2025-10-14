using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Tests;

public sealed class SocialMediaMigrationsTests
{
    [Fact]
    public async Task LatestMigration_AppliesAndSeedsSocialMediaDefaults()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var context = new ApplicationDbContext(options))
        {
            await context.Database.MigrateAsync();
        }

        await using (var context = new ApplicationDbContext(options))
        {
            var pending = await context.Database.GetPendingMigrationsAsync();
            Assert.Empty(pending);

            var eventTypes = await context.SocialMediaEventTypes
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .ToListAsync();

            Assert.Equal(3, eventTypes.Count);
            Assert.All(eventTypes, type => Assert.True(type.IsActive));

            Assert.Contains(eventTypes, type => type.Name == "Campaign Launch" && type.CreatedByUserId == "system");
            Assert.Contains(eventTypes, type => type.Name == "Milestone Update");
            Assert.Contains(eventTypes, type => type.Name == "Community Engagement");
        }
    }
}
