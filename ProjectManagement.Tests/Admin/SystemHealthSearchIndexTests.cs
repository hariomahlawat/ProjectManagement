using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Navigation.ModuleNav;
using ProjectManagement.Services.SearchV2.Models;
using Xunit;

namespace ProjectManagement.Tests.Admin;

// System health includes search (docs/ux/admin-module.md, PR 7): items that could not be added to
// search are missing from results, so they turn the check amber instead of staying hidden on the
// Search index page.
public sealed class SystemHealthSearchIndexTests
{
    [Fact]
    public void FailedItems_MakeTheSearchCheckAWarning()
    {
        var (status, summary, detail) = AdminSystemHealthService.SearchIndexResult(Health(ready: true, failed: 27));

        Assert.Equal(AdminHealthStatus.Warning, status);
        Assert.Equal("27 items could not be added to search, so they are missing from results.", summary);
        Assert.Equal("1,708 items searchable · 0 waiting to be indexed.", detail);
    }

    [Fact]
    public void AnIndexThatIsNotReady_IsAWarning()
    {
        var (status, summary, _) = AdminSystemHealthService.SearchIndexResult(Health(ready: false, failed: 0));

        Assert.Equal(AdminHealthStatus.Warning, status);
        Assert.StartsWith("The search index is not ready yet", summary);
    }

    [Fact]
    public void AReadyIndexWithNoFailures_IsHealthy()
    {
        var (status, summary, _) = AdminSystemHealthService.SearchIndexResult(Health(ready: true, failed: 0));

        Assert.Equal(AdminHealthStatus.Healthy, status);
        Assert.Equal("Search is up to date.", summary);
    }

    [Theory]
    [InlineData("Expected cadence: 1 day(s) · Retention is disabled by configuration.", "Expected cadence: 1 day(s) · Retention is disabled by configuration.")]
    [InlineData("Expected cadence: 1 day(s)", "Expected cadence: 1 day(s).")]
    public void WorkerDetails_EndWithOneFullStop(string text, string expected)
    {
        Assert.Equal(expected, AdminSystemHealthService.EndSentence(text));
    }

    [Fact]
    public void SearchIndex_IsReachedFromSystemHealth()
    {
        var entry = AdminNavigationCatalog.GetEntry(AdminNavigationKeys.SearchIndex);

        Assert.False(entry.InSidebar);
        Assert.Equal(AdminNavigationKeys.DatabaseHealth, entry.SidebarParent);
    }

    private static SearchIndexHealth Health(bool ready, long failed) =>
        new(ready, 18, 7, 1708, 0, failed, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
}
