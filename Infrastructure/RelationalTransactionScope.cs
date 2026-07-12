using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace ProjectManagement.Infrastructure;

public sealed class RelationalTransactionScope : IAsyncDisposable
{
    private readonly IDbContextTransaction? _transaction;

    private RelationalTransactionScope(IDbContextTransaction? transaction)
    {
        _transaction = transaction;
    }

    public static Task<RelationalTransactionScope> CreateAsync(
        DatabaseFacade database,
        CancellationToken cancellationToken = default) =>
        CreateCoreAsync(database, isolationLevel: null, cancellationToken);

    public static Task<RelationalTransactionScope> CreateAsync(
        DatabaseFacade database,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default) =>
        CreateCoreAsync(database, isolationLevel, cancellationToken);

    private static async Task<RelationalTransactionScope> CreateCoreAsync(
        DatabaseFacade database,
        IsolationLevel? isolationLevel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        if (!database.IsRelational())
        {
            return new RelationalTransactionScope(null);
        }

        var transaction = isolationLevel.HasValue
            ? await database.BeginTransactionAsync(isolationLevel.Value, cancellationToken)
            : await database.BeginTransactionAsync(cancellationToken);

        return new RelationalTransactionScope(transaction);
    }

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        _transaction?.CommitAsync(cancellationToken) ?? Task.CompletedTask;

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        _transaction?.RollbackAsync(cancellationToken) ?? Task.CompletedTask;

    public ValueTask DisposeAsync() =>
        _transaction?.DisposeAsync() ?? ValueTask.CompletedTask;
}
