using ProjectManagement.Features.MediaLibrary.Services;
using Xunit;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class FaceCandidateRefreshRuntimeStateTests
{
    [Fact]
    public void RuntimeState_TracksRecoveryCompletionAndFailureWithoutLosingWorkerIdentity()
    {
        var state = new FaceCandidateRefreshRuntimeState();
        state.MarkConfigured(true);
        state.MarkStarted("worker-1");
        state.MarkRecovered(2);
        state.MarkBatchStarted();
        state.MarkBatchCompleted(6);
        state.MarkFailed(new TimeoutException("candidate timeout"));

        var snapshot = state.GetSnapshot();

        Assert.True(snapshot.WorkerConfigured);
        Assert.True(snapshot.WorkerStarted);
        Assert.Equal("worker-1", snapshot.WorkerId);
        Assert.Equal(6, snapshot.ProcessedSinceStart);
        Assert.Equal(2, snapshot.RecoveredStaleSinceStart);
        Assert.Equal(1, snapshot.FailureCountSinceStart);
        Assert.Equal("TimeoutException", snapshot.LastFailureCode);
        Assert.Contains("candidate timeout", snapshot.LastFailureMessage!);
        Assert.NotNull(snapshot.LastHeartbeatUtc);
    }

    [Fact]
    public async Task RequestRun_WakesWaitingWorkerPromptly()
    {
        var state = new FaceCandidateRefreshRuntimeState();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var wait = state.WaitForRunRequestAsync(TimeSpan.FromSeconds(30), cts.Token);
        state.RequestRun();
        await wait;

        Assert.True(wait.IsCompletedSuccessfully);
    }
}
