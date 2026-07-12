using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.Admin.Pages.Diagnostics;
using ProjectManagement.Data;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class DbHealthPageTests
{
    [Fact]
    public async Task NonRelationalProvider_ReturnsUnavailableHealthSnapshot()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        var service = new DatabaseHealthService(context, NullLogger<DatabaseHealthService>.Instance);
        var model = new DbHealthModel(service);

        await model.OnGetAsync(CancellationToken.None);

        Assert.False(model.Snapshot.IsRelational);
        Assert.Equal("(not available)", model.Snapshot.LatestMigration);
        Assert.Empty(model.Snapshot.PendingMigrations);
        Assert.Contains(model.Snapshot.Checks, check => check.Status == AdminHealthStatus.Unavailable);
    }
}
