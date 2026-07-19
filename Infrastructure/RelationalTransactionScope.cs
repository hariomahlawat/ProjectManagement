using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace ProjectManagement.Infrastructure;

/// <summary>
/// Provides a composable EF Core transaction boundary.
///
/// A top-level scope owns the database transaction. A nested scope participates in
/// the existing transaction through a savepoint and never commits or disposes the
/// outer transaction. Callbacks registered through <see cref="RegisterAfterCommit"/>
/// are executed only after the managed root transaction commits successfully.
/// </summary>
public sealed class RelationalTransactionScope : IAsyncDisposable
{
    private enum ScopeKind
    {
        NoTransaction = 0,
        Owner = 1,
        Savepoint = 2
    }

    private enum CompletionState
    {
        Active = 0,
        Committed = 1,
        RolledBack = 2
    }

    private static readonly ConditionalWeakTable<IDbContextTransaction, TransactionState> TransactionStates = new();

    private readonly IDbContextTransaction? _transaction;
    private readonly TransactionState _state;
    private readonly ScopeKind _kind;
    private readonly string? _savepointName;
    private readonly int _callbackCheckpoint;
    private readonly List<Exception> _postCommitErrors = new();

    private CompletionState _completionState;
    private bool _transactionDisposed;
    private bool _disposed;

    private RelationalTransactionScope(
        IDbContextTransaction? transaction,
        TransactionState state,
        ScopeKind kind,
        string? savepointName = null,
        int callbackCheckpoint = 0)
    {
        _transaction = transaction;
        _state = state;
        _kind = kind;
        _savepointName = savepointName;
        _callbackCheckpoint = callbackCheckpoint;
    }

    public bool OwnsTransaction => _kind == ScopeKind.Owner;

    public bool IsNested => _kind == ScopeKind.Savepoint;

    public bool IsCommitted => _completionState == CompletionState.Committed;

    public bool IsRolledBack => _completionState == CompletionState.RolledBack;

    /// <summary>
    /// Best-effort post-commit callback failures. Database commit is never reported
    /// as failed merely because a secondary post-commit action failed.
    /// </summary>
    public IReadOnlyList<Exception> PostCommitErrors => _postCommitErrors;

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
            return new RelationalTransactionScope(
                transaction: null,
                state: new TransactionState(hasManagedOwner: true),
                kind: ScopeKind.NoTransaction);
        }

        var currentTransaction = database.CurrentTransaction;
        if (currentTransaction is not null)
        {
            if (!currentTransaction.SupportsSavepoints)
            {
                throw new InvalidOperationException(
                    "The active database transaction does not support savepoints. " +
                    "Nested transactional operations cannot be completed safely.");
            }

            var state = TransactionStates.GetValue(
                currentTransaction,
                static _ => new TransactionState(hasManagedOwner: false));

            var callbackCheckpoint = state.CallbackCount;
            var savepointName = state.CreateSavepointName();
            await currentTransaction.CreateSavepointAsync(savepointName, cancellationToken);

            return new RelationalTransactionScope(
                currentTransaction,
                state,
                ScopeKind.Savepoint,
                savepointName,
                callbackCheckpoint);
        }

        var transaction = isolationLevel.HasValue
            ? await database.BeginTransactionAsync(isolationLevel.Value, cancellationToken)
            : await database.BeginTransactionAsync(cancellationToken);

        var rootState = new TransactionState(hasManagedOwner: true);
        TransactionStates.Remove(transaction);
        TransactionStates.Add(transaction, rootState);

        return new RelationalTransactionScope(
            transaction,
            rootState,
            ScopeKind.Owner);
    }

    /// <summary>
    /// Registers a secondary action that must run only after the managed root
    /// transaction has committed. The callback should be idempotent and should
    /// perform its own logging when it encounters an error.
    /// </summary>
    public void RegisterAfterCommit(Func<CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ThrowIfDisposed();

        if (_completionState != CompletionState.Active)
        {
            throw new InvalidOperationException(
                "Post-commit callbacks must be registered before the transaction scope is completed.");
        }

        if (_kind == ScopeKind.Savepoint && !_state.HasManagedOwner)
        {
            throw new InvalidOperationException(
                "A post-commit callback cannot be registered because the active outer transaction " +
                "was not created by RelationalTransactionScope.");
        }

        _state.AddCallback(callback);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_completionState != CompletionState.Active)
        {
            return;
        }

        switch (_kind)
        {
            case ScopeKind.NoTransaction:
                _completionState = CompletionState.Committed;
                await ExecutePostCommitCallbacksAsync(cancellationToken);
                return;

            case ScopeKind.Savepoint:
                await _transaction!.ReleaseSavepointAsync(_savepointName!, cancellationToken);
                _completionState = CompletionState.Committed;
                return;

            case ScopeKind.Owner:
                await _transaction!.CommitAsync(cancellationToken);
                _completionState = CompletionState.Committed;
                TransactionStates.Remove(_transaction);
                await DisposeOwnedTransactionAsync();
                await ExecutePostCommitCallbacksAsync(cancellationToken);
                return;

            default:
                throw new InvalidOperationException("Unsupported transaction scope state.");
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_completionState != CompletionState.Active)
        {
            return;
        }

        switch (_kind)
        {
            case ScopeKind.NoTransaction:
                _state.TruncateCallbacks(0);
                _completionState = CompletionState.RolledBack;
                return;

            case ScopeKind.Savepoint:
                await RollbackSavepointAsync(cancellationToken);
                _state.TruncateCallbacks(_callbackCheckpoint);
                _completionState = CompletionState.RolledBack;
                return;

            case ScopeKind.Owner:
                await _transaction!.RollbackAsync(cancellationToken);
                _state.TruncateCallbacks(0);
                _completionState = CompletionState.RolledBack;
                TransactionStates.Remove(_transaction);
                await DisposeOwnedTransactionAsync();
                return;

            default:
                throw new InvalidOperationException("Unsupported transaction scope state.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_completionState == CompletionState.Active)
            {
                await RollbackAsync(CancellationToken.None);
            }
        }
        finally
        {
            if (_kind == ScopeKind.Owner && _transaction is not null)
            {
                TransactionStates.Remove(_transaction);
                await DisposeOwnedTransactionAsync();
            }

            _disposed = true;
        }
    }

    private async ValueTask DisposeOwnedTransactionAsync()
    {
        if (_transactionDisposed || _transaction is null)
        {
            return;
        }

        await _transaction.DisposeAsync();
        _transactionDisposed = true;
    }

    private async Task RollbackSavepointAsync(CancellationToken cancellationToken)
    {
        await _transaction!.RollbackToSavepointAsync(_savepointName!, cancellationToken);

        // PostgreSQL retains a savepoint after ROLLBACK TO SAVEPOINT. Release it so
        // the outer transaction remains clean for subsequent operations.
        await _transaction.ReleaseSavepointAsync(_savepointName!, cancellationToken);
    }

    private async Task ExecutePostCommitCallbacksAsync(CancellationToken cancellationToken)
    {
        var callbacks = _state.DrainCallbacks();
        foreach (var callback in callbacks)
        {
            try
            {
                await callback(cancellationToken);
            }
            catch (Exception exception)
            {
                // The database transaction has already committed. Preserve the
                // successful primary operation and expose the secondary failure to
                // callers that wish to inspect it.
                _postCommitErrors.Add(exception);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RelationalTransactionScope));
        }
    }

    private sealed class TransactionState
    {
        private readonly object _gate = new();
        private readonly List<Func<CancellationToken, Task>> _callbacks = new();
        private long _savepointSequence;

        public TransactionState(bool hasManagedOwner)
        {
            HasManagedOwner = hasManagedOwner;
        }

        public bool HasManagedOwner { get; }

        public int CallbackCount
        {
            get
            {
                lock (_gate)
                {
                    return _callbacks.Count;
                }
            }
        }

        public string CreateSavepointName()
        {
            var sequence = Interlocked.Increment(ref _savepointSequence);
            return ($"prism_sp_{sequence:x}_{Guid.NewGuid():N}")[..32];
        }

        public void AddCallback(Func<CancellationToken, Task> callback)
        {
            lock (_gate)
            {
                _callbacks.Add(callback);
            }
        }

        public void TruncateCallbacks(int count)
        {
            lock (_gate)
            {
                var safeCount = Math.Clamp(count, 0, _callbacks.Count);
                if (safeCount < _callbacks.Count)
                {
                    _callbacks.RemoveRange(safeCount, _callbacks.Count - safeCount);
                }
            }
        }

        public IReadOnlyList<Func<CancellationToken, Task>> DrainCallbacks()
        {
            lock (_gate)
            {
                if (_callbacks.Count == 0)
                {
                    return Array.Empty<Func<CancellationToken, Task>>();
                }

                var callbacks = _callbacks.ToArray();
                _callbacks.Clear();
                return callbacks;
            }
        }
    }
}
