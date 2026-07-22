using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AdminWorkerStatusRegistryTests
{
    [Fact]
    public void Registry_TracksSuccessfulCycle()
    {
        var registry = new AdminWorkerStatusRegistry();
        registry.Register("worker", "Test worker");
        registry.MarkStarted("worker");
        registry.MarkSucceeded("worker", "Processed 3 items.");

        var status = Assert.Single(registry.GetSnapshot());
        Assert.Equal(AdminWorkerState.Healthy, status.State);
        Assert.NotNull(status.LastStartedUtc);
        Assert.NotNull(status.LastSucceededUtc);
        Assert.Equal("Processed 3 items.", status.Detail);
    }

    [Fact]
    public void Registry_DoesNotExposeExceptionMessage()
    {
        var registry = new AdminWorkerStatusRegistry();
        registry.Register("worker", "Test worker");
        registry.MarkFailed("worker", new InvalidOperationException("sensitive detail"));

        var status = Assert.Single(registry.GetSnapshot());
        Assert.Equal(AdminWorkerState.Failed, status.State);
        Assert.Equal(nameof(InvalidOperationException), status.Detail);
        Assert.NotNull(status.Detail);
        Assert.False(status.Detail!.Contains("sensitive", StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void Registry_PreservesExpectedSchedule()
    {
        var registry = new AdminWorkerStatusRegistry();
        registry.Register("worker", "Test worker", TimeSpan.FromHours(6));

        var status = Assert.Single(registry.GetSnapshot());

        Assert.Equal(TimeSpan.FromHours(6), status.ExpectedInterval);
        Assert.Equal(AdminWorkerState.Registered, status.State);
    }

}
