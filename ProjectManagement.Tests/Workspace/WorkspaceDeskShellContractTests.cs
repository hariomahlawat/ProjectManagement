using Xunit;

namespace ProjectManagement.Tests.Workspace;

/// <summary>
/// The My Workspace pages share one width on every screen: capped and centred on a wide monitor,
/// with the top bar at the same width so the side rail lines up with the menu above it.
/// </summary>
public sealed class WorkspaceDeskShellContractTests
{
    [Theory]
    [InlineData("Pages", "Workspace", "Index.cshtml")]
    [InlineData("Pages", "Workspace", "CommandDesk.cshtml")]
    [InlineData("Pages", "Workspace", "Conference.cshtml")]
    [InlineData("Pages", "Workspace", "BriefingDecks", "Index.cshtml")]
    public void MyWorkspacePages_UseTheDeskShell(params string[] path)
    {
        var markup = ReadRepoFile(path);

        Assert.Contains("ViewData[\"PageShell\"] = \"desk\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Layout_GivesTheChromeTheDeskWidthOnDeskPages()
    {
        var layout = ReadRepoFile("Pages", "Shared", "_Layout.cshtml");

        Assert.Contains("\"desk\" => \"desk\"", layout, StringComparison.Ordinal);
        Assert.Contains("shellVariant == \"desk\" ? \"pm-chrome-shell pm-chrome-shell--desk\"", layout, StringComparison.Ordinal);
        Assert.Contains("<div class=\"@chromeShellClass pm-topbar__inner\">", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("<div class=\"pm-chrome-shell", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void SiteCss_CapsTheDeskAndChromeAtTheSameWidth()
    {
        var css = ReadRepoFile("wwwroot", "css", "site.css").Replace("\r\n", "\n");

        Assert.Contains("--pm-shell-desk: 115rem;", css, StringComparison.Ordinal);
        Assert.Contains(".pm-page-shell--desk,\n.pm-chrome-shell--desk {\n    width: min(calc(100% - (2 * var(--pm-shell-desk-gutter))), var(--pm-shell-desk));", css, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandDesk_UsesTwoColumnsOnlyWhenThereIsRoom()
    {
        var css = ReadRepoFile("wwwroot", "css", "command-desk.css");
        var markup = ReadRepoFile("Pages", "Workspace", "CommandDesk.cshtml");

        Assert.Contains("container: cd-desk / inline-size;", css, StringComparison.Ordinal);
        Assert.Contains("@container cd-desk (min-width: 84rem)", css, StringComparison.Ordinal);
        Assert.Contains("<div class=\"cd-board\">", markup, StringComparison.Ordinal);
        foreach (var section in new[] { "cd-section--good", "cd-section--waiting", "cd-section--lagging", "cd-section--quiet" })
        {
            Assert.Contains(section, markup, StringComparison.Ordinal);
        }

        // Reading order stays top to bottom: moved forward, waiting, lagging, quiet.
        var order = new[] { "cd-good-title", "cd-waiting-title", "cd-lagging-title", "cd-quiet-title" }
            .Select(id => markup.IndexOf($"id=\"{id}\"", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(order.OrderBy(index => index), order);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var root = ResolveRepoRoot();
        var path = Path.Combine(new[] { root }.Concat(segments).ToArray());
        return File.ReadAllText(path);
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ProjectManagement.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ProjectManagement repository root.");
    }
}
