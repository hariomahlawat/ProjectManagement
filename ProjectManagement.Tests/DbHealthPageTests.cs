using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class DbHealthPageTests
{
    [Fact]
    public async Task NonRelationalProvider_ReturnsUnavailableDatabaseHealthSnapshot()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        var service = new DatabaseHealthService(context, NullLogger<DatabaseHealthService>.Instance);

        var snapshot = await service.CheckAsync(CancellationToken.None);

        Assert.False(snapshot.IsRelational);
        Assert.Equal("(not available)", snapshot.LatestMigration);
        Assert.Empty(snapshot.PendingMigrations);
        Assert.Contains(snapshot.Checks, check => check.Status == AdminHealthStatus.Unavailable);
    }
}
