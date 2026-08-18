using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class FaceIdentityGroupingRuntimeStateTests
{
    [Fact]
    public void InitialSnapshot_IsIdleUntilAnOperationalWorkerRequestsTheFirstRefresh()
    {
        var state = new FaceIdentityGroupingRuntimeState();

        var snapshot = state.GetSnapshot();

        Assert.False(snapshot.IsReady);
        Assert.False(snapshot.IsRefreshPending);
        Assert.Null(snapshot.InvalidatedAtUtc);
    }

    [Fact]
    public void Successful_snapshot_is_retained_when_a_later_refresh_fails()
    {
        var state = new FaceIdentityGroupingRuntimeState();
        var refreshedAt = DateTimeOffset.UtcNow;
        var result = EmptyResult();

        state.SetResult(result, refreshedAt);
        state.SetFailure("temporary database failure", refreshedAt.AddSeconds(5));

        var snapshot = state.GetSnapshot();
        Assert.Same(result, snapshot.Result);
        Assert.Equal(refreshedAt, snapshot.RefreshedAtUtc);
        Assert.Equal("temporary database failure", snapshot.FailureReason);
        Assert.True(snapshot.IsReady);
        Assert.True(snapshot.IsRefreshPending);
    }

    [Fact]
    public void Invalidate_retains_the_last_snapshot_and_marks_refresh_pending()
    {
        var state = new FaceIdentityGroupingRuntimeState();
        var result = EmptyResult();
        state.SetResult(result, DateTimeOffset.UtcNow);
        var before = state.GetSnapshot();

        state.Invalidate();

        var snapshot = state.GetSnapshot();
        Assert.Same(result, snapshot.Result);
        Assert.True(snapshot.IsReady);
        Assert.True(snapshot.IsRefreshPending);
        Assert.NotNull(snapshot.InvalidatedAtUtc);
        Assert.Equal(before.RefreshGeneration + 1, snapshot.RefreshGeneration);
    }

    [Fact]
    public void Stale_inflight_refresh_cannot_clear_a_newer_invalidation()
    {
        var state = new FaceIdentityGroupingRuntimeState();
        state.SetResult(EmptyResult(), DateTimeOffset.UtcNow);
        var refreshGeneration = state.GetSnapshot().RefreshGeneration;

        state.Invalidate();
        var newerGeneration = state.GetSnapshot().RefreshGeneration;
        state.SetResult(EmptyResult(), DateTimeOffset.UtcNow.AddSeconds(1), refreshGeneration);

        var snapshot = state.GetSnapshot();
        Assert.True(snapshot.IsReady);
        Assert.True(snapshot.IsRefreshPending);
        Assert.Equal(newerGeneration, snapshot.RefreshGeneration);
        Assert.NotNull(snapshot.InvalidatedAtUtc);
    }

    [Fact]
    public void Refresh_for_current_generation_clears_pending_state()
    {
        var state = new FaceIdentityGroupingRuntimeState();
        state.SetResult(EmptyResult(), DateTimeOffset.UtcNow);
        state.Invalidate();
        var generation = state.GetSnapshot().RefreshGeneration;

        state.SetResult(EmptyResult(), DateTimeOffset.UtcNow.AddSeconds(1), generation);

        var snapshot = state.GetSnapshot();
        Assert.False(snapshot.IsRefreshPending);
        Assert.Null(snapshot.InvalidatedAtUtc);
        Assert.Equal(generation, snapshot.RefreshGeneration);
    }

    [Fact]
    public async Task Invalidate_wakes_a_worker_waiting_for_the_next_refresh_request()
    {
        var state = new FaceIdentityGroupingRuntimeState();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var wait = state.WaitForRefreshRequestAsync(TimeSpan.FromMinutes(1), timeout.Token);

        state.Invalidate();

        await wait.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(state.GetSnapshot().IsRefreshPending);
    }

    private static FaceIdentityGroupingResult EmptyResult()
        => new(Array.Empty<FaceIdentityGroup>(), 0, 0, 0);
}
