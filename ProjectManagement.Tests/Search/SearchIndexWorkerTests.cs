using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Services.SearchV2;
using ProjectManagement.Services.SearchV2.Indexing;
using ProjectManagement.Services.SearchV2.Models;
using Xunit;

namespace ProjectManagement.Tests.Search;

public sealed class SearchIndexWorkerTests
{
    private const int OwnVersion = 5;

    [Fact]
    public async Task Tick_DoesNotThrow_WhenTheDatabaseIsUnavailable()
    {
        var harness = new Harness();
        harness.Store.StateFailure = new InvalidOperationException("connection refused");

        var healthy = await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.False(healthy);
        Assert.Equal(0, harness.Builder.BuildAllCalls);
    }

    [Fact]
    public async Task Tick_IsolatesEachStep_SoADequeueFailureStillRunsReconciliation()
    {
        var harness = new Harness();
        harness.Store.State = new SearchIndexActiveState(7, OwnVersion, harness.Time.Now.AddDays(-2));
        harness.Store.DequeueFailure = new InvalidOperationException("deadlock");

        var healthy = await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.False(healthy);
        Assert.Equal(1, harness.Builder.BuildAllCalls);
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(5, 300)]
    [InlineData(50, 300)]
    public void TickDelay_BacksOffExponentiallyAndCapsAtFiveMinutes(int failures, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), SearchIndexWorker.TickDelay(TimeSpan.FromSeconds(15), failures));
    }

    [Fact]
    public async Task FailedAutomaticRebuild_BacksOffInsteadOfRetryingEveryTick()
    {
        var harness = new Harness();
        harness.Builder.Failure = new InvalidOperationException("projection failed");

        await harness.Worker.RunOnceAsync(CancellationToken.None);
        await harness.Worker.RunOnceAsync(CancellationToken.None);
        await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, harness.Builder.BuildAllCalls);
        Assert.Equal(harness.Time.Now.AddMinutes(15), harness.Worker.NextAutomaticRebuildUtc);
        Assert.Single(harness.Store.RecordedErrors);

        harness.Time.Now = harness.Time.Now.AddMinutes(16);
        await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, harness.Builder.BuildAllCalls);
        Assert.Equal(harness.Time.Now.AddMinutes(30), harness.Worker.NextAutomaticRebuildUtc);

        harness.Builder.Failure = null;
        harness.Time.Now = harness.Time.Now.AddMinutes(31);
        await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(3, harness.Builder.BuildAllCalls);
        Assert.Equal(OwnVersion, harness.Store.State.IndexVersion);
        Assert.Equal(DateTimeOffset.MinValue, harness.Worker.NextAutomaticRebuildUtc);
    }

    [Fact]
    public async Task NewerActiveVersion_MakesTheProcessPassive()
    {
        var harness = new Harness();
        harness.Store.State = new SearchIndexActiveState(9, OwnVersion + 1, harness.Time.Now.AddDays(-5));
        harness.Store.Queue.Enqueue(new SearchIndexWorkItem(1, "Project", "42", 0));

        var healthy = await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.True(healthy);
        Assert.True(harness.Worker.IsPassive);
        Assert.Equal(0, harness.Builder.BuildAllCalls);
        Assert.Equal(0, harness.Builder.BuildEntityCalls);
        Assert.Single(harness.Store.Queue);
        Assert.Equal(OwnVersion + 1, harness.Store.State.IndexVersion);
    }

    [Fact]
    public async Task OlderActiveVersion_IsRebuiltAtThisProcessVersion()
    {
        var harness = new Harness();
        harness.Store.State = new SearchIndexActiveState(3, OwnVersion - 1, harness.Time.Now);

        await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, harness.Builder.BuildAllCalls);
        Assert.Equal(OwnVersion, harness.Store.State.IndexVersion);
        Assert.False(harness.Store.LastAllowDowngrade);
    }

    [Fact]
    public async Task Rebuild_IsSkippedWhileAnotherProcessHoldsTheRebuildLock()
    {
        var harness = new Harness();
        harness.Coordinator.RebuildAvailable = false;

        var healthy = await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.True(healthy);
        Assert.Equal(0, harness.Builder.BuildAllCalls);
    }

    [Fact]
    public async Task Rebuild_RechecksStateUnderTheLock_AndGoesPassiveWhenANewerVersionWon()
    {
        var harness = new Harness();
        harness.Store.State = new SearchIndexActiveState(3, OwnVersion - 1, harness.Time.Now);
        harness.Coordinator.OnRebuildAcquired = () =>
            harness.Store.State = new SearchIndexActiveState(4, OwnVersion + 1, harness.Time.Now);

        await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, harness.Builder.BuildAllCalls);
        Assert.True(harness.Worker.IsPassive);
    }

    [Fact]
    public async Task SupersededActivation_IsNotTreatedAsAFailure()
    {
        var harness = new Harness();
        harness.Store.SupersedeNextActivation = true;

        await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, harness.Builder.BuildAllCalls);
        Assert.Equal(DateTimeOffset.MinValue, harness.Worker.NextAutomaticRebuildUtc);
        Assert.Empty(harness.Store.RecordedErrors);
    }

    [Fact]
    public async Task IncrementalIndexing_IsSkippedWhileAFullRebuildRuns()
    {
        var harness = new Harness();
        harness.Store.State = new SearchIndexActiveState(7, OwnVersion, harness.Time.Now);
        harness.Store.Queue.Enqueue(new SearchIndexWorkItem(1, "Project", "42", 0));
        harness.Coordinator.IncrementalAvailable = false;

        await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, harness.Builder.BuildEntityCalls);
        Assert.Single(harness.Store.Queue);
    }

    [Fact]
    public async Task IncrementalIndexing_ProcessesQueuedEntities()
    {
        var harness = new Harness();
        harness.Store.State = new SearchIndexActiveState(7, OwnVersion, harness.Time.Now);
        harness.Store.Queue.Enqueue(new SearchIndexWorkItem(1, "Project", "42", 0));
        harness.Store.Queue.Enqueue(new SearchIndexWorkItem(2, "Project", "43", 0));

        var healthy = await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.True(healthy);
        Assert.Equal(2, harness.Builder.BuildEntityCalls);
        Assert.Equal(new long[] { 1, 2 }, harness.Store.Completed);
    }

    [Fact]
    public async Task ProjectChange_ReindexesItsDependentRowsInTheSamePass()
    {
        var harness = new Harness();
        harness.Store.State = new SearchIndexActiveState(7, OwnVersion, harness.Time.Now);
        harness.Builder.Dependents[42] = new[]
        {
            new SearchEntityRef("ProjectTot", "42"),
            new SearchEntityRef("ProliferationGranular", "8f0c5a43-31b6-4d55-9a37-5b1b0f6c2d11"),
            new SearchEntityRef("Training", "3e1f9a2b-7c44-4f0e-8b9d-2a6c5d4e3f10")
        };
        harness.Store.Queue.Enqueue(new SearchIndexWorkItem(1, "Project", "42", 0));

        Assert.True(await harness.Worker.RunOnceAsync(CancellationToken.None));

        Assert.Equal(
            new[]
            {
                "Project/42",
                "ProjectTot/42",
                "ProliferationGranular/8f0c5a43-31b6-4d55-9a37-5b1b0f6c2d11",
                "Training/3e1f9a2b-7c44-4f0e-8b9d-2a6c5d4e3f10"
            },
            harness.Store.Replaced);
        Assert.Equal(new long[] { 1 }, harness.Store.Completed);
    }

    [Fact]
    public async Task IprChange_RefreshesTheProjectItLeftAndTheProjectItJoined()
    {
        var harness = new Harness();
        harness.Store.State = new SearchIndexActiveState(7, OwnVersion, harness.Time.Now);
        harness.Store.IndexedParents["IprRecord/5"] = new[] { 10 };
        harness.Builder.Projections["IprRecord/5"] = new[] { Projection("IprRecord", "5", parentProjectId: 11) };
        harness.Store.Queue.Enqueue(new SearchIndexWorkItem(1, "IprRecord", "5", 0));

        Assert.True(await harness.Worker.RunOnceAsync(CancellationToken.None));

        // The projects first: until the IPR row is replaced, the index still names project 10.
        Assert.Equal(new[] { "Project/10", "Project/11", "IprRecord/5" }, harness.Store.Replaced);
        Assert.Equal(new long[] { 1 }, harness.Store.Completed);
    }

    [Fact]
    public async Task IprChange_WhoseOldProjectFailsToRefresh_KeepsItsOldRowForTheRetry()
    {
        var harness = new Harness();
        harness.Store.State = new SearchIndexActiveState(7, OwnVersion, harness.Time.Now);
        harness.Store.IndexedParents["IprRecord/5"] = new[] { 10 };
        harness.Store.FailReplacing = "Project/10";
        harness.Builder.Projections["IprRecord/5"] = new[] { Projection("IprRecord", "5", parentProjectId: 11) };
        harness.Store.Queue.Enqueue(new SearchIndexWorkItem(1, "IprRecord", "5", 0));

        await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.DoesNotContain("IprRecord/5", harness.Store.Replaced);
        Assert.Equal(new long[] { 1 }, harness.Store.Failed);
    }

    [Fact]
    public async Task ProjectChange_ThatPicksUpAMovedIpr_RefreshesTheProjectTheIprLeft()
    {
        var harness = new Harness();
        harness.Store.State = new SearchIndexActiveState(7, OwnVersion, harness.Time.Now);
        harness.Builder.Dependents[11] = new[] { new SearchEntityRef("IprRecord", "5") };
        harness.Store.IndexedParents["IprRecord/5"] = new[] { 10 };
        harness.Builder.Projections["IprRecord/5"] = new[] { Projection("IprRecord", "5", parentProjectId: 11) };
        harness.Store.Queue.Enqueue(new SearchIndexWorkItem(1, "Project", "11", 0));

        Assert.True(await harness.Worker.RunOnceAsync(CancellationToken.None));

        // Project 11 is current already; project 10 is refreshed before the IPR row moves.
        Assert.Equal(new[] { "Project/11", "Project/10", "IprRecord/5" }, harness.Store.Replaced);
    }

    [Fact]
    public async Task OtherRecords_DoNotTouchTheirProjectRow()
    {
        var harness = new Harness();
        harness.Store.State = new SearchIndexActiveState(7, OwnVersion, harness.Time.Now);
        harness.Builder.Projections["Training/7"] = new[] { Projection("Training", "7", parentProjectId: 10) };
        harness.Store.Queue.Enqueue(new SearchIndexWorkItem(1, "Training", "7", 0));

        Assert.True(await harness.Worker.RunOnceAsync(CancellationToken.None));

        Assert.Equal(new[] { "Training/7" }, harness.Store.Replaced);
    }

    private static SearchProjection Projection(string entityType, string key, int? parentProjectId) =>
        new(
            entityType, key, entityType, key, parentProjectId,
            "IPR", "Trackers", "Title", "title", null, $"/{entityType}/{key}",
            null, null, null, null, "title", null, null, null, DateTimeOffset.UtcNow,
            SearchVisibilityMode.Authenticated, null, null, OwnVersion,
            Array.Empty<SearchProjectionTerm>(), Array.Empty<SearchProjectionPrincipal>(), "{}");

    [Fact]
    public async Task RequestedRebuild_IsReleasedWithoutCountingAnAttempt_WhenAnotherProcessIsRebuilding()
    {
        var harness = new Harness();
        harness.Store.State = new SearchIndexActiveState(7, OwnVersion, harness.Time.Now);
        harness.Store.Queue.Enqueue(new SearchIndexWorkItem(5, SearchIndexStore.FullRebuildEntityType, "active", 0));
        harness.Coordinator.RebuildAvailable = false;

        await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(new long[] { 5 }, harness.Store.Released);
        Assert.Empty(harness.Store.Failed);
        Assert.Empty(harness.Store.Completed);
    }

    [Fact]
    public async Task RequestedRebuild_BypassesAutomaticBackoff_AndCompletes()
    {
        var harness = new Harness();
        harness.Builder.Failure = new InvalidOperationException("projection failed");
        await harness.Worker.RunOnceAsync(CancellationToken.None);
        Assert.True(harness.Worker.NextAutomaticRebuildUtc > harness.Time.Now);

        harness.Builder.Failure = null;
        harness.Store.State = new SearchIndexActiveState(7, OwnVersion, harness.Time.Now);
        harness.Store.Queue.Enqueue(new SearchIndexWorkItem(5, SearchIndexStore.FullRebuildEntityType, "active", 0));

        await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, harness.Builder.BuildAllCalls);
        Assert.Equal(new long[] { 5 }, harness.Store.Completed);
        Assert.True(harness.Store.RecoveredFullRebuildLeases);
    }

    [Fact]
    public async Task RequestedRebuild_BypassesBackoff_WhileTheInitialRebuildIsFailing()
    {
        var harness = new Harness();
        harness.Builder.Failure = new InvalidOperationException("projection failed");
        await harness.Worker.RunOnceAsync(CancellationToken.None);
        Assert.True(harness.Worker.NextAutomaticRebuildUtc > harness.Time.Now);
        Assert.False(harness.Store.State.HasActiveGeneration);

        harness.Builder.Failure = null;
        harness.Store.Queue.Enqueue(new SearchIndexWorkItem(5, SearchIndexStore.FullRebuildEntityType, "active", 0));

        await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, harness.Builder.BuildAllCalls);
        Assert.Equal(new long[] { 5 }, harness.Store.Completed);
        Assert.Equal(OwnVersion, harness.Store.State.IndexVersion);
    }

    [Fact]
    public async Task AbandonedRebuildRequests_AreRecoveredUnderTheLock_EvenWhilePassive()
    {
        var harness = new Harness();
        harness.Store.State = new SearchIndexActiveState(9, OwnVersion + 1, harness.Time.Now);

        await harness.Worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, harness.Store.FullRebuildRecoveryCalls);

        await harness.Worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, harness.Store.FullRebuildRecoveryCalls);

        harness.Time.Now = harness.Time.Now.AddMinutes(11);
        await harness.Worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, harness.Store.FullRebuildRecoveryCalls);

        harness.Coordinator.RebuildAvailable = false;
        harness.Time.Now = harness.Time.Now.AddMinutes(11);
        await harness.Worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, harness.Store.FullRebuildRecoveryCalls);
    }

    [Fact]
    public async Task PassiveProcess_HonoursAnExplicitRebuildRequest_AsTheRollbackRecoveryPath()
    {
        var harness = new Harness();
        harness.Store.State = new SearchIndexActiveState(9, OwnVersion + 1, harness.Time.Now);
        harness.Store.Queue.Enqueue(new SearchIndexWorkItem(1, "Project", "42", 0));
        harness.Store.Queue.Enqueue(new SearchIndexWorkItem(5, SearchIndexStore.FullRebuildEntityType, "active", 0));

        await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, harness.Builder.BuildAllCalls);
        Assert.True(harness.Store.LastAllowDowngrade);
        Assert.Equal(OwnVersion, harness.Store.State.IndexVersion);
        Assert.Equal(new long[] { 5 }, harness.Store.Completed);
        Assert.Equal(0, harness.Builder.BuildEntityCalls);
    }

    private sealed class Harness
    {
        public Harness()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ISearchIndexStore>(Store);
            services.AddSingleton<ISearchProjectionBuilder>(Builder);
            services.AddSingleton<ISearchIndexCoordinator>(Coordinator);
            var provider = services.BuildServiceProvider();
            Worker = new SearchIndexWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new SearchV2Options { ProjectionVersion = OwnVersion, FullRebuildRetryMinutes = 15 }),
                NullLogger<SearchIndexWorker>.Instance,
                Time);
            Store.Time = Time;
        }

        public ManualTime Time { get; } = new();
        public FakeStore Store { get; } = new();
        public FakeBuilder Builder { get; } = new();
        public FakeCoordinator Coordinator { get; } = new();
        public SearchIndexWorker Worker { get; }
    }

    internal sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class FakeBuilder : ISearchProjectionBuilder
    {
        public int BuildAllCalls { get; private set; }
        public int BuildEntityCalls { get; private set; }
        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<SearchProjection>> BuildAllAsync(CancellationToken cancellationToken)
        {
            BuildAllCalls++;
            if (Failure is not null) throw Failure;
            return Task.FromResult<IReadOnlyList<SearchProjection>>(Array.Empty<SearchProjection>());
        }

        public Dictionary<int, SearchEntityRef[]> Dependents { get; } = new();
        public Dictionary<string, SearchProjection[]> Projections { get; } = new(StringComparer.Ordinal);
        public List<string> Built { get; } = new();

        public Task<IReadOnlyList<SearchProjection>> BuildEntityAsync(string entityType, string entityKey, CancellationToken cancellationToken)
        {
            BuildEntityCalls++;
            Built.Add($"{entityType}/{entityKey}");
            return Task.FromResult<IReadOnlyList<SearchProjection>>(
                Projections.TryGetValue($"{entityType}/{entityKey}", out var rows) ? rows : Array.Empty<SearchProjection>());
        }

        public Task<IReadOnlyList<SearchEntityRef>> FindProjectDependentsAsync(int projectId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SearchEntityRef>>(
                Dependents.TryGetValue(projectId, out var dependents) ? dependents : Array.Empty<SearchEntityRef>());
    }

    internal sealed class FakeCoordinator : ISearchIndexCoordinator
    {
        public bool RebuildAvailable { get; set; } = true;
        public bool IncrementalAvailable { get; set; } = true;
        public Action? OnRebuildAcquired { get; set; }

        public Task<IAsyncDisposable?> TryAcquireFullRebuildAsync(CancellationToken cancellationToken)
        {
            if (!RebuildAvailable) return Task.FromResult<IAsyncDisposable?>(null);
            OnRebuildAcquired?.Invoke();
            return Task.FromResult<IAsyncDisposable?>(new Lease());
        }

        public Task<IAsyncDisposable?> TryAcquireIncrementalAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable?>(IncrementalAvailable ? new Lease() : null);

        private sealed class Lease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    internal sealed class FakeStore : ISearchIndexStore
    {
        public TimeProvider Time { get; set; } = TimeProvider.System;
        public SearchIndexActiveState State { get; set; } = SearchIndexActiveState.None;
        public Exception? StateFailure { get; set; }
        public Exception? DequeueFailure { get; set; }
        public bool SupersedeNextActivation { get; set; }
        public bool LastAllowDowngrade { get; private set; }
        public bool RecoveredFullRebuildLeases { get; private set; }
        public int FullRebuildRecoveryCalls { get; private set; }
        public Queue<SearchIndexWorkItem> Queue { get; } = new();
        public List<long> Completed { get; } = new();
        public List<long> Failed { get; } = new();
        public List<long> Released { get; } = new();
        public List<string> RecordedErrors { get; } = new();

        public Task<SearchIndexActiveState> GetActiveStateAsync(CancellationToken cancellationToken) =>
            StateFailure is null ? Task.FromResult(State) : Task.FromException<SearchIndexActiveState>(StateFailure);

        public Task ReplaceFullGenerationAsync(IReadOnlyList<SearchProjection> projections, int indexVersion, CancellationToken cancellationToken, bool allowDowngrade = false)
        {
            LastAllowDowngrade = allowDowngrade;
            if (SupersedeNextActivation)
            {
                SupersedeNextActivation = false;
                throw new SearchIndexSupersededException(indexVersion);
            }

            State = new SearchIndexActiveState(State.ActiveGeneration + 1, indexVersion, Time.GetUtcNow());
            return Task.CompletedTask;
        }

        public Task<SearchIndexWorkItem?> DequeueAsync(CancellationToken cancellationToken, string? entityType = null)
        {
            if (DequeueFailure is not null) return Task.FromException<SearchIndexWorkItem?>(DequeueFailure);
            if (entityType is not null)
            {
                var match = Queue.FirstOrDefault(item => item.EntityType == entityType);
                if (match is null) return Task.FromResult<SearchIndexWorkItem?>(null);
                var remaining = Queue.Where(item => item != match).ToArray();
                Queue.Clear();
                foreach (var item in remaining) Queue.Enqueue(item);
                return Task.FromResult<SearchIndexWorkItem?>(match);
            }

            return Task.FromResult(Queue.TryDequeue(out var next) ? next : null);
        }

        public Task<int> RecoverStaleWorkItemsAsync(TimeSpan leaseTimeout, CancellationToken cancellationToken, bool includeFullRebuildRequests = false)
        {
            RecoveredFullRebuildLeases |= includeFullRebuildRequests;
            if (includeFullRebuildRequests) FullRebuildRecoveryCalls++;
            return Task.FromResult(0);
        }

        public Task CompleteAsync(SearchIndexWorkItem item, CancellationToken cancellationToken)
        {
            Completed.Add(item.Id);
            return Task.CompletedTask;
        }

        public Task FailAsync(long workItemId, string error, CancellationToken cancellationToken)
        {
            Failed.Add(workItemId);
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(long workItemId, TimeSpan retryAfter, CancellationToken cancellationToken)
        {
            Released.Add(workItemId);
            return Task.CompletedTask;
        }

        public Task RecordIndexErrorAsync(string error, CancellationToken cancellationToken)
        {
            RecordedErrors.Add(error);
            return Task.CompletedTask;
        }

        public Dictionary<string, int[]> IndexedParents { get; } = new(StringComparer.Ordinal);
        public List<string> Replaced { get; } = new();

        public string? FailReplacing { get; set; }

        public Task ReplaceEntityAsync(string requestedEntityType, string requestedEntityKey, IReadOnlyList<SearchProjection> projections, int indexVersion, CancellationToken cancellationToken)
        {
            var key = $"{requestedEntityType}/{requestedEntityKey}";
            if (key == FailReplacing) throw new InvalidOperationException("database unavailable");
            Replaced.Add(key);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<int>> GetIndexedParentProjectIdsAsync(string entityType, string entityKey, int indexVersion, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<int>>(
                IndexedParents.TryGetValue($"{entityType}/{entityKey}", out var ids) ? ids : Array.Empty<int>());

        public Task<bool> IsReadyAsync(int indexVersion, CancellationToken cancellationToken) =>
            Task.FromResult(State.HasActiveGeneration && State.IndexVersion == indexVersion);

        public Task<SearchIndexHealth> GetHealthAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnqueueAsync(string entityType, string entityKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RecordReconciliationAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RequestFullRebuildAsync(string requestedBy, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<SearchFailedIndexWorkItem>> GetFailedItemsAsync(int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RetryFailedAsync(long? workItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
