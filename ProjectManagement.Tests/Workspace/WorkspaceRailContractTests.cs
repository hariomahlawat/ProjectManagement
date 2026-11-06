using System.Text.RegularExpressions;
using Xunit;

namespace ProjectManagement.Tests.Workspace;

/// <summary>
/// The folded workspace rail shows icons only: each destination needs its own icon, and every
/// link must keep its name for screen readers (the labels are hidden visually, never removed).
/// </summary>
public sealed class WorkspaceRailContractTests
{
    [Fact]
    public void EveryRailDestination_HasItsOwnIcon()
    {
        var rail = ReadRepoFile("Pages", "Workspace", "_CommandWorkspaceRail.cshtml");
        var nav = rail[rail.IndexOf("<nav id=\"workspace-local-navigation\">", StringComparison.Ordinal)..];

        // Pairs of (icon, label) inside the navigation; "My ERP activity" appears twice (one per
        // lens) for the same destination, so labels are compared, not links.
        var items = Regex.Matches(nav, "<i class=\"bi (bi-[a-z0-9-]+)\" aria-hidden=\"true\"></i><span>([^<]+)</span>")
            .Select(match => (Icon: match.Groups[1].Value, Label: match.Groups[2].Value))
            .Distinct()
            .ToList();

        Assert.True(items.Count >= 12, $"Expected the rail's destinations, found {items.Count}.");
        var shared = items.GroupBy(item => item.Icon).Where(group => group.Select(item => item.Label).Distinct().Count() > 1).ToList();
        Assert.True(shared.Count == 0,
            "Each destination needs its own icon: " + string.Join("; ", shared.Select(group => $"{group.Key} for {string.Join(", ", group.Select(item => item.Label))}")));
    }

    [Fact]
    public void FoldedRail_HidesLabelsVisuallyButKeepsTheirNames()
    {
        var css = ReadRepoFile("wwwroot", "css", "command-workspace.css").Replace("\r\n", "\n");

        // display:none would remove the link's only text, leaving screen readers an unnamed link.
        Assert.DoesNotContain(".command-workspace:not(.is-nav-expanded) .cw-local-rail nav a > span { display:none; }", css, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"nav a > span,\s*\n\s*\.[^{]*nav a > b \{ display:none; \}"), css);
        Assert.Contains(".command-workspace:not(.is-nav-expanded) .cw-local-rail nav a > span,", css, StringComparison.Ordinal);
        Assert.Contains("clip:rect(0 0 0 0);", css, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ProjectManagement.csproj")))
            {
                return File.ReadAllText(Path.Combine(new[] { current.FullName }.Concat(segments).ToArray()));
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ProjectManagement repository root.");
    }
}
