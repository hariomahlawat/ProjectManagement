using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ApplicationDatabaseMigrationsTests
{
    [Fact]
    public async Task ApplyingMigrations_LeavesNoPendingMigrations()
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
        }
    }
}
