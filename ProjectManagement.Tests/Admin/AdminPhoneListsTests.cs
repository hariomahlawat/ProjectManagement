using System.Text.RegularExpressions;
using Xunit;

namespace ProjectManagement.Tests.Admin;

// Admin pages on phones (docs/ux/admin-review-3-fixes.md, PR 8): the bins name the open list,
// and lookup rows become cards whose actions are in view without sideways scrolling.
public sealed class AdminPhoneListsTests
{
    [Theory]
    [InlineData("Areas/Admin/Pages/Shared/_AdminReferenceList.cshtml", new[] { "Order", "Used by", "Status" })]
    public void LookupTables_BecomeCardsOnPhones_WithEveryValueLabelled(string view, string[] labels)
    {
        var text = Read(view);

        Assert.Matches(new Regex(@"<table class=""admin-data-table admin-data-table--cards"), text);
        foreach (var label in labels)
        {
            Assert.Contains($"data-label=\"{label}\"", text);
        }

        // The actions cell keeps its class, which the phone layout pins to the card's top right.
        Assert.Contains("<td class=\"admin-data-table__actions\">", text);
    }

    [Fact]
    public void PhoneCards_PinTheActionsInView_AndUseOnlyTokens()
    {
        var css = Read("wwwroot/css/admin.css");
        var start = css.IndexOf("/* Phones (docs/ux/admin-review-3-fixes.md, PR 8). */", StringComparison.Ordinal);
        Assert.True(start >= 0, "The PR 8 phone block is missing from admin.css.");
        var block = css[start..];

        Assert.Contains(".admin-data-table--cards td.admin-data-table__actions { position: absolute;", block);
        Assert.Contains(".admin-data-table-wrap:has(> .admin-data-table--cards) { overflow-x: visible; }", block);
        // The desktop table's min-width (52rem on the lookup table) must not carry over to the cards.
        Assert.Contains(".admin-data-table--cards, .admin-data-table--cards tbody { display: block; width: 100%; min-width: 0; }", block);
        Assert.DoesNotMatch(new Regex("#[0-9a-fA-F]{3,8}\\b|rgba?\\("), block);
    }

    [Fact]
    public void RestoreTabs_NameTheOpenListAndAreFoundByTheScript()
    {
        var view = Read("Pages/Shared/Components/RestoreTabs/Default.cshtml");
        var script = Read("wwwroot/js/admin/admin-shell.js");

        Assert.Contains("class=\"admin-restore-tabs__current\"", view);
        Assert.Contains("@current.Label", view);
        Assert.Contains("data-admin-restore-tabs", view);
        Assert.Contains("[data-admin-restore-tabs]", script);
        Assert.Contains("inline: \"center\", block: \"nearest\"", script);
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
