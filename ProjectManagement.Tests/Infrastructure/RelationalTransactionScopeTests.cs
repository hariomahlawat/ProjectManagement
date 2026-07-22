using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Infrastructure;
using Xunit;

namespace ProjectManagement.Tests.Infrastructure;

public sealed class RelationalTransactionScopeTests
{
    [Fact]
    public async Task NestedCommit_DoesNotCommitOuterTransaction()
    {
        await using var scope = await TestDbScope.CreateAsync();

        await using (var outer = await RelationalTransactionScope.CreateAsync(scope.Db.Database))
        {
            scope.Db.Rows.Add(new TransactionRow { Name = "outer" });
            await scope.Db.SaveChangesAsync();

            await using (var nested = await RelationalTransactionScope.CreateAsync(scope.Db.Database))
            {
                scope.Db.Rows.Add(new TransactionRow { Name = "nested" });
                await scope.Db.SaveChangesAsync();
                await nested.CommitAsync();
            }

            await outer.RollbackAsync();
        }

        scope.Db.ChangeTracker.Clear();
        Assert.Empty(await scope.Db.Rows.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task NestedRollback_RollsBackOnlyToItsSavepoint()
    {
        await using var scope = await TestDbScope.CreateAsync();

        await using (var outer = await RelationalTransactionScope.CreateAsync(scope.Db.Database))
        {
            scope.Db.Rows.Add(new TransactionRow { Name = "outer" });
            await scope.Db.SaveChangesAsync();

            await using (var nested = await RelationalTransactionScope.CreateAsync(scope.Db.Database))
            {
                scope.Db.Rows.Add(new TransactionRow { Name = "nested" });
                await scope.Db.SaveChangesAsync();
                await nested.RollbackAsync();
            }

            scope.Db.ChangeTracker.Clear();
            await outer.CommitAsync();
        }

        var rows = await scope.Db.Rows.AsNoTracking().OrderBy(row => row.Name).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("outer", rows[0].Name);
    }

    [Fact]
    public async Task NestedPostCommitCallback_RunsOnlyAfterRootCommit()
    {
        await using var scope = await TestDbScope.CreateAsync();
        var callbackCount = 0;

        await using (var outer = await RelationalTransactionScope.CreateAsync(scope.Db.Database))
        {
            await using (var nested = await RelationalTransactionScope.CreateAsync(scope.Db.Database))
            {
                nested.RegisterAfterCommit(_ =>
                {
                    callbackCount++;
                    return Task.CompletedTask;
                });

                await nested.CommitAsync();
            }

            Assert.Equal(0, callbackCount);
            await outer.CommitAsync();
        }

        Assert.Equal(1, callbackCount);
    }

    [Fact]
    public async Task RootRollback_DiscardsNestedPostCommitCallback()
    {
        await using var scope = await TestDbScope.CreateAsync();
        var callbackCount = 0;

        await using (var outer = await RelationalTransactionScope.CreateAsync(scope.Db.Database))
        {
            await using (var nested = await RelationalTransactionScope.CreateAsync(scope.Db.Database))
            {
                nested.RegisterAfterCommit(_ =>
                {
                    callbackCount++;
                    return Task.CompletedTask;
                });

                await nested.CommitAsync();
            }

            await outer.RollbackAsync();
        }

        Assert.Equal(0, callbackCount);
    }

    [Fact]
    public async Task PostCommitCallback_CanUseTheSameDbContextAfterRootTransactionIsDisposed()
    {
        await using var scope = await TestDbScope.CreateAsync();

        await using var transaction = await RelationalTransactionScope.CreateAsync(scope.Db.Database);
        scope.Db.Rows.Add(new TransactionRow { Name = "primary" });
        await scope.Db.SaveChangesAsync();
        transaction.RegisterAfterCommit(async cancellationToken =>
        {
            scope.Db.Rows.Add(new TransactionRow { Name = "after-commit" });
            await scope.Db.SaveChangesAsync(cancellationToken);
        });

        await transaction.CommitAsync();

        var names = await scope.Db.Rows.AsNoTracking().OrderBy(row => row.Name).Select(row => row.Name).ToListAsync();
        Assert.Equal(new[] { "after-commit", "primary" }, names);
    }

    [Fact]
    public async Task PostCommitCallbackFailure_DoesNotConvertCommittedTransactionIntoFailure()
    {
        await using var scope = await TestDbScope.CreateAsync();

        await using var transaction = await RelationalTransactionScope.CreateAsync(scope.Db.Database);
        scope.Db.Rows.Add(new TransactionRow { Name = "committed" });
        await scope.Db.SaveChangesAsync();
        transaction.RegisterAfterCommit(_ => throw new InvalidOperationException("secondary failure"));

        await transaction.CommitAsync();

        Assert.True(transaction.IsCommitted);
        Assert.Single(transaction.PostCommitErrors);
        Assert.Single(await scope.Db.Rows.AsNoTracking().ToListAsync());
    }

    private sealed class TransactionTestDbContext(DbContextOptions<TransactionTestDbContext> options)
        : DbContext(options)
    {
        public DbSet<TransactionRow> Rows => Set<TransactionRow>();
    }

    private sealed class TransactionRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TestDbScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestDbScope(TransactionTestDbContext db, SqliteConnection connection)
        {
            Db = db;
            _connection = connection;
        }

        public TransactionTestDbContext Db { get; }

        public static async Task<TestDbScope> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<TransactionTestDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new TransactionTestDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new TestDbScope(db, connection);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
