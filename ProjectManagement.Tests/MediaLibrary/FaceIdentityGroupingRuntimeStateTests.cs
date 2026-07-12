using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class FaceIdentityGroupingRuntimeStateTests
{
    [Fact]
    public void Successful_snapshot_is_retained_when_a_later_refresh_fails()
    {
        var state = new FaceIdentityGroupingRuntimeState();
        var refreshedAt = DateTimeOffset.UtcNow;
        var result = new FaceIdentityGroupingResult(
            Array.Empty<FaceIdentityGroup>(),
            0,
            0,
            0);

        state.SetResult(result, refreshedAt);
        state.SetFailure("temporary database failure", refreshedAt.AddSeconds(5));

        var snapshot = state.GetSnapshot();
        Assert.Same(result, snapshot.Result);
        Assert.Equal(refreshedAt, snapshot.RefreshedAtUtc);
        Assert.Equal("temporary database failure", snapshot.FailureReason);
        Assert.True(snapshot.IsReady);
    }
    [Fact]
    public void Invalidate_removes_a_stale_grouping_snapshot()
    {
        var state = new FaceIdentityGroupingRuntimeState();
        state.SetResult(
            new FaceIdentityGroupingResult(Array.Empty<FaceIdentityGroup>(), 0, 0, 0),
            DateTimeOffset.UtcNow);

        state.Invalidate();

        var snapshot = state.GetSnapshot();
        Assert.False(snapshot.IsReady);
        Assert.Null(snapshot.Result);
    }

}
