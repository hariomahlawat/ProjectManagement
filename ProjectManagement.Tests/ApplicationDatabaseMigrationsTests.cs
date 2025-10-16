using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;
using ProjectManagement.Migrations;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ApplicationDatabaseMigrationsTests
{
    [Fact]
    public void ModelSnapshot_IsInSyncWithRuntimeModel()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var context = new ApplicationDbContext(options);
        var snapshot = new ApplicationDbContextModelSnapshot();

        var differ = context.GetService<IMigrationsModelDiffer>();

        var snapshotModel = snapshot.Model.GetRelationalModel();
        var runtimeModel = context.Model.GetRelationalModel();

        Assert.False(differ.HasDifferences(snapshotModel, runtimeModel));
    }
}
