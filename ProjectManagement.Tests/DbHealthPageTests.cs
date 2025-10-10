using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.Admin.Pages.Diagnostics;
using ProjectManagement.Data;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class DbHealthPageTests
{
    [Fact]
    public async Task NonRelationalProvider_SetsFallbackValues()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        var model = new DbHealthModel(context);

        await model.OnGetAsync();

        Assert.False(model.IsRelational);
        Assert.Equal("(not available)", model.LatestMigration);
        Assert.Empty(model.PendingMigrations);
    }
}
