using ProjectManagement.Areas.DocumentRepository.Pages.Admin;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.DocRepo;
using Xunit;

namespace ProjectManagement.Tests.Admin;

// System health's background jobs in plain words (docs/ux/admin-review-3-fixes.md, PR 6).
public sealed class SystemHealthJobMessagesTests
{
    [Theory]
    [InlineData(0, "Nothing to remove")]
    [InlineData(1, "Removed 1 item from the notebook bin")]
    [InlineData(3, "Removed 3 items from the notebook bin")]
    [InlineData(1200, "Removed 1,200 items from the notebook bin")]
    public void Removed_CountsWithTheRightNoun(int count, string expected)
    {
        Assert.Equal(expected, BackgroundJobMessages.Removed(count, "item", "items", "from the notebook bin"));
    }

    [Fact]
    public void Removed_WithoutPlace_ReadsAsASentence()
    {
        Assert.Equal("Removed 2 old audit records", BackgroundJobMessages.Removed(2, "old audit record", "old audit records"));
    }

    [Fact]
    public void SignInSummary_UsesShortDates()
    {
        var day = new DateOnly(2026, 11, 6);

        Assert.Equal("Sign-in summary for 6 Nov is up to date", BackgroundJobMessages.SignInSummaryUpToDate(day));
        Assert.Equal("Sign-in summary for 6 Nov added: 1 sign-in", BackgroundJobMessages.SignInSummaryAdded(day, 1));
        Assert.Equal("Sign-in summary for 6 Nov added: 14 sign-ins", BackgroundJobMessages.SignInSummaryAdded(day, 14));
    }

    [Theory]
    [InlineData(0, 0, "Nothing to remove")]
    [InlineData(4, 0, "Removed 4 old notifications")]
    [InlineData(0, 1, "Marked 1 old unread notification as read")]
    [InlineData(1, 2, "Removed 1 old notification; marked 2 old unread notifications as read")]
    public void NotificationsTidied_ReadsPlainly(int removed, int markedRead, string expected)
    {
        Assert.Equal(expected, BackgroundJobMessages.NotificationsTidied(removed, markedRead));
    }

    [Fact]
    public void SwitchedOffJob_ShowsNoScheduleAndNoWarning()
    {
        var registry = new AdminWorkerStatusRegistry();
        registry.Register("docrepo-trash-purge", "Document trash purge", TimeSpan.FromHours(6));
        registry.MarkSwitchedOff("docrepo-trash-purge", "Documents stay in the bin until someone deletes them");
        var worker = Assert.Single(registry.GetSnapshot());
        Assert.Equal(AdminWorkerState.SwitchedOff, worker.State);

        // Even long after start-up, an off job is never "older than expected".
        var check = AdminSystemHealthService.WorkerCheck(worker, DateTimeOffset.UtcNow.AddDays(10));

        Assert.Equal(AdminHealthStatus.Healthy, check.Status);
        Assert.Equal("Switched off.", check.Summary);
        Assert.Equal("Documents stay in the bin until someone deletes them.", check.Detail);
        Assert.DoesNotContain("Runs every", check.Detail);
        Assert.Null(check.RecommendedAction);
    }

    [Fact]
    public void SwitchedOnAgain_ShowsItsScheduleOnceItRuns()
    {
        var registry = new AdminWorkerStatusRegistry();
        registry.Register("docrepo-trash-purge", "Document trash purge", TimeSpan.FromHours(6));
        registry.MarkSwitchedOff("docrepo-trash-purge");
        registry.MarkStarted("docrepo-trash-purge");
        registry.MarkSucceeded("docrepo-trash-purge", BackgroundJobMessages.NothingToRemove);

        var check = AdminSystemHealthService.WorkerCheck(Assert.Single(registry.GetSnapshot()), DateTimeOffset.UtcNow);

        Assert.Equal("Its last run finished without problems.", check.Summary);
        Assert.StartsWith("Runs every 6 hours", check.Detail);
        Assert.EndsWith("Nothing to remove.", check.Detail);
    }

    [Fact]
    public void SignInSummaryJob_IsNamedInPlainWords()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Services", "LoginAggregationWorker.cs"));

        Assert.Contains("\"Sign-in summary\"", source, StringComparison.Ordinal);
        Assert.Contains("\"login-aggregation\"", source, StringComparison.Ordinal); // the key stays
    }

    [Fact]
    public void MissingFiles_ShowsIstWithZone_AndTheRealCacheTime()
    {
        // The checked-at time itself goes through IAdminTimeService.FormatIst
        // (AdminRound4RobustnessTests covers the text).
        Assert.Equal("2 minutes", MissingFilesModel.FormatCacheDuration(DocRepoMissingFilesService.CacheDuration));
        Assert.Equal("1 minute", MissingFilesModel.FormatCacheDuration(TimeSpan.FromMinutes(1)));
        Assert.Equal("45 seconds", MissingFilesModel.FormatCacheDuration(TimeSpan.FromSeconds(45)));

        var view = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Areas", "DocumentRepository", "Pages", "Admin", "MissingFiles.cshtml"));
        Assert.DoesNotContain("two minutes", view, StringComparison.OrdinalIgnoreCase);
        // The cache time is in the admin page header, built in the page model (fourth review, PR 6).
        var model = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Areas", "DocumentRepository", "Pages", "Admin", "MissingFiles.cshtml.cs"));
        Assert.Contains("{CacheMinutesText}", model, StringComparison.Ordinal);
        Assert.DoesNotContain("two minutes", model, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@Model.CheckedAtIst", view, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
