using System.Text.RegularExpressions;
using Xunit;

namespace ProjectManagement.Tests.Admin;

// Phones, round two (docs/ux/admin-review-4-fixes.md, PR 8): the wide admin tables become cards,
// tile labels wrap, the bin tab strip and the usage heatmap stop widening the page, and Media
// sources' header row no longer sticks over the first row.
public sealed class AdminPhonesRoundTwoTests
{
    [Theory]
    [InlineData("Areas/Admin/Pages/Logs/Index.cshtml", "admin-audit-table", new[] { "Actor", "Affected record", "Time", "Severity" })]
    [InlineData("Areas/Admin/Pages/Analytics/Logins.cshtml", "admin-monitoring-table", new[] { "When", "Outcome", "Review reason", "Source" })]
    [InlineData("Pages/Usage/Index.cshtml", "erp-usage-table", new[] { "Today / last active", "Active working days", "Approx. active time", "Modules / actions", "How often" })]
    [InlineData("Pages/Admin/MediaSources/Index.cshtml", "media-unavailable-table", new[] { "Source", "Status", "Last checked" })]
    public void WideTables_BecomeCardsOnPhones_WithEveryValueLabelled(string view, string tableClass, string[] labels)
    {
        var text = Read(view);

        Assert.Matches(new Regex($@"<table class=""[^""]*admin-data-table--cards[^""]*{tableClass}"""), text);
        foreach (var label in labels)
        {
            Assert.Contains($"data-label=\"{label}\"", text);
        }
    }

    [Fact]
    public void AdminCss_HasBalancedBraces_SoLaterRulesAreNotPhoneOnly()
    {
        // A missing "}" after the PR 8 (third review) card block once put every later rule inside
        // the phone media query.
        var css = Read("wwwroot/css/admin.css");

        Assert.Equal(css.Count(c => c == '{'), css.Count(c => c == '}'));
    }

    [Fact]
    public void RoundTwoCss_KeepsWhoAndWhenInView_AndUsesOnlyTokens()
    {
        var css = Read("wwwroot/css/admin.css");
        var start = css.IndexOf("/* Phones, round two (docs/ux/admin-review-4-fixes.md, PR 8)", StringComparison.Ordinal);
        Assert.True(start >= 0, "The round-two phone block is missing from admin.css.");
        var end = css.IndexOf("\n}\n", start, StringComparison.Ordinal);
        var block = css[start..end];

        Assert.Contains(".admin-audit-table td[data-label=\"Actor\"] { order: 1; }", block);
        Assert.Contains(".admin-audit-table td[data-label=\"Time\"] { order: 2; }", block);
        Assert.Contains(".admin-monitoring-table td.admin-monitoring-table__who { order: -1;", block);
        Assert.DoesNotMatch(new Regex("#[0-9a-fA-F]{3,8}\\b|rgba?\\("), block);
    }

    [Fact]
    public void ScreenReaderLabels_StayInsideTheirScrollers()
    {
        var admin = Read("wwwroot/css/admin.css");
        var usage = Read("wwwroot/css/erp-usage.css");

        Assert.Matches(new Regex(@"\.admin-restore-tabs \{[^}]*position: relative;"), admin);
        Assert.Matches(new Regex(@"\.erp-heatmap-scroll \{[^}]*position: relative;"), usage);
    }

    [Fact]
    public void TileLabels_WrapInsteadOfBeingCutShort()
    {
        var css = Read("wwwroot/css/admin.css");

        Assert.DoesNotMatch(new Regex(@"\.admin-user-status-card__copy span \{[^}]*text-overflow: ellipsis"), css);
        Assert.DoesNotMatch(new Regex(@"\.admin-monitoring-metric__copy strong \{[^}]*text-overflow: ellipsis"), css);
    }

    [Fact]
    public void MediaSourcesHeaderRow_IsNotStickyInsideItsScroller()
    {
        var css = Read("wwwroot/css/pages/media-sources.css");

        Assert.DoesNotMatch(new Regex(@"\.media-unavailable-table thead th \{[^}]*position: sticky"), css);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
