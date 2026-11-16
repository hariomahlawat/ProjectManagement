using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ProjectManagement.Data;
using Xunit;

namespace ProjectManagement.Tests.Tools;

/// <summary>
/// Collage Studio's tables in the EF model match the model snapshot, so the next generated
/// migration does not try to create them again. This test looks only at Collage tables on
/// purpose: the snapshot lags the model elsewhere in the repository (migrations there are written
/// by hand), and a whole-model check would fail for reasons that are not Collage's.
/// Nothing connects to a database; the PostgreSQL provider is only used to build both models.
/// </summary>
public sealed class CollageModelSnapshotTests
{
    [Fact]
    public void The_collage_tables_in_the_model_match_the_snapshot()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=prism_model_only")
            .Options);

        var model = db.GetService<IDesignTimeModel>().Model;
        var collageTables = model.GetEntityTypes()
            .Select(type => type.GetTableName())
            .Where(IsCollageTable)
            .Distinct()
            .ToList();
        Assert.Contains("CollageStickers", collageTables);

        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;
        Assert.NotNull(snapshot);
        if (snapshot is IMutableModel mutable)
        {
            snapshot = mutable.FinalizeModel();
        }
        snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot, designTime: true, validationLogger: null);

        var differences = db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshot.GetRelationalModel(), model.GetRelationalModel());

        var collageDifferences = differences
            .Where(operation => operation is ITableMigrationOperation table && IsCollageTable(table.Table)
                || operation is CreateTableOperation create && IsCollageTable(create.Name)
                || operation is DropTableOperation drop && IsCollageTable(drop.Name))
            .Select(Describe)
            .ToList();

        Assert.True(collageDifferences.Count == 0,
            "The model snapshot does not match the model for Collage tables: " + string.Join("; ", collageDifferences));
    }

    private static bool IsCollageTable(string? table) =>
        table is not null && table.StartsWith("Collage", StringComparison.Ordinal);

    private static string Describe(MigrationOperation operation) => operation switch
    {
        CreateTableOperation create => $"create table {create.Name}",
        AddColumnOperation add => $"add column {add.Table}.{add.Name}",
        AlterColumnOperation alter => $"alter column {alter.Table}.{alter.Name}",
        DropColumnOperation drop => $"drop column {drop.Table}.{drop.Name}",
        CreateIndexOperation index => $"create index {index.Name} on {index.Table}",
        DropIndexOperation index => $"drop index {index.Name} on {index.Table}",
        _ => operation.GetType().Name
    };
}
