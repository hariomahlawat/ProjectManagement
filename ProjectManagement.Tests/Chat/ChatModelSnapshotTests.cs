using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ProjectManagement.Data;
using Xunit;

namespace ProjectManagement.Tests.Chat;

/// <summary>
/// Chat's tables in the EF model match the model snapshot, so the next generated migration does
/// not try to create or change them again (chat migrations since AddChatRepliesReactionsMentions
/// are written by hand, with the snapshot updated alongside). Like CollageModelSnapshotTests, it
/// looks only at Chat tables: the snapshot lags the model elsewhere in the repository.
/// Nothing connects to a database; the PostgreSQL provider is only used to build both models.
/// </summary>
public sealed class ChatModelSnapshotTests
{
    [Fact]
    public void The_chat_tables_in_the_model_match_the_snapshot()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=prism_model_only")
            .Options);

        var model = db.GetService<IDesignTimeModel>().Model;
        var chatTables = model.GetEntityTypes()
            .Select(type => type.GetTableName())
            .Where(IsChatTable)
            .Distinct()
            .ToList();
        Assert.Contains("ChatMessages", chatTables);

        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;
        Assert.NotNull(snapshot);
        if (snapshot is IMutableModel mutable)
        {
            snapshot = mutable.FinalizeModel();
        }
        snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot, designTime: true, validationLogger: null);

        var differences = db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshot.GetRelationalModel(), model.GetRelationalModel());

        var chatDifferences = differences
            .Where(operation => operation is ITableMigrationOperation table && IsChatTable(table.Table)
                || operation is CreateTableOperation create && IsChatTable(create.Name)
                || operation is DropTableOperation drop && IsChatTable(drop.Name))
            .Select(Describe)
            .ToList();

        Assert.True(chatDifferences.Count == 0,
            "The model snapshot does not match the model for Chat tables: " + string.Join("; ", chatDifferences));
    }

    private static bool IsChatTable(string? table) =>
        table is not null && table.StartsWith("Chat", StringComparison.Ordinal);

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
