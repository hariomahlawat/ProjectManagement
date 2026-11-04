using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Notifications;

namespace ProjectManagement.Services.Notifications;

/// <summary>
/// Writes notification outbox rows in the same database transaction as the business change
/// they describe, without ever letting a notification failure fail that change.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="QueueAfterSaveAsync"/> inside the business transaction (a
/// <see cref="RelationalTransactionScope"/>), after the business entities are tracked and
/// before the scope commits. It:
/// </para>
/// <list type="number">
/// <item>saves the caller's pending changes into the open transaction, so generated ids exist
/// and a business failure still fails the operation;</item>
/// <item>opens a savepoint and runs <c>queue</c>, which adds <see cref="NotificationDispatch"/>
/// rows through <see cref="INotificationOutboxWriter"/>, then saves them;</item>
/// <item>on any failure, rolls back to the savepoint, drops the queued rows and logs.
/// PostgreSQL aborts a transaction on any failed statement, so the savepoint is what keeps
/// the business transaction usable after a notification lookup or insert fails.</item>
/// </list>
/// <para>
/// The notification therefore commits if and only if the business change commits, and its
/// failure is contained exactly as the earlier post-commit publishing was. The dispatcher
/// delivers the rows only after commit. The <c>queue</c> delegate must let exceptions
/// propagate; swallowing one would hide a failed statement and leave the transaction aborted.
/// </para>
/// </remarks>
public sealed class TransactionalNotificationQueue
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<TransactionalNotificationQueue> _logger;

    public TransactionalNotificationQueue(
        ApplicationDbContext db,
        ILogger<TransactionalNotificationQueue>? logger = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? NullLogger<TransactionalNotificationQueue>.Instance;
    }

    /// <summary>
    /// Saves pending business changes, then queues notifications in a savepoint.
    /// Returns <c>true</c> when the notification rows were written, <c>false</c> when queueing
    /// failed and was rolled back (the business change is unaffected).
    /// </summary>
    public async Task<bool> QueueAfterSaveAsync(
        string operation,
        Func<CancellationToken, Task> queue,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(queue);

        // Business changes first, outside the savepoint: their failure is the operation's failure.
        await _db.SaveChangesAsync(cancellationToken);

        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction is null)
        {
            // Without an open transaction the business change has already committed, so the
            // rows below are written on their own, as post-commit publishing did.
            _logger.LogWarning(
                "Notification for {Operation} was queued outside a transaction and is not atomic with the change.",
                operation);
        }

        await using var savepoint = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        try
        {
            await queue(cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await savepoint.CommitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DiscardAsync(savepoint);
            throw;
        }
        catch (Exception exception)
        {
            // Rolling back to the savepoint must succeed for the business transaction to stay
            // usable; if it cannot, that failure propagates and the operation fails as a whole.
            await DiscardAsync(savepoint);
            _logger.LogError(
                exception,
                "Notification for {Operation} could not be queued; the change is kept without it.",
                operation);
            return false;
        }
    }

    /// <summary>
    /// Saves pending business changes and queues their notifications in one transaction, then
    /// commits it. Use it where the change would otherwise be a single
    /// <c>SaveChangesAsync</c>. Inside an outer transaction this becomes a savepoint and the
    /// outer transaction decides the commit.
    /// </summary>
    public async Task<bool> SaveAndQueueAsync(
        string operation,
        Func<CancellationToken, Task> queue,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        var queued = await QueueAfterSaveAsync(operation, queue, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return queued;
    }

    private async Task DiscardAsync(RelationalTransactionScope savepoint)
    {
        await savepoint.RollbackAsync(CancellationToken.None);

        foreach (var entry in _db.ChangeTracker.Entries<NotificationDispatch>()
                     .Where(entry => entry.State == EntityState.Added)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
