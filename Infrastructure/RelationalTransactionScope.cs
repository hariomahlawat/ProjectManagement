using System;
using System.Threading;
using System.Threading.Tasks;
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

    public static async Task<RelationalTransactionScope> CreateAsync(
        DatabaseFacade database,
        CancellationToken cancellationToken = default)
    {
        if (database == null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        if (!database.IsRelational())
        {
            return new RelationalTransactionScope(null);
        }

        var transaction = await database.BeginTransactionAsync(cancellationToken);
        return new RelationalTransactionScope(transaction);
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        return _transaction?.CommitAsync(cancellationToken) ?? Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        return _transaction?.RollbackAsync(cancellationToken) ?? Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return _transaction?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
