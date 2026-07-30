using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppProductionUxContractTests
{
    [Fact]
    public void ManageWorkspace_UsesProductionShellAndPersistentCommands()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Manage.cshtml");

        Assert.Contains("ViewData[\"PageShell\"] = \"workspace\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewData[\"UseFullWidth\"]", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-commandbar", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-fullscreen-toggle", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-jump-input", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-validation-navigator", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-table-scrollbar", markup, StringComparison.Ordinal);
        Assert.Contains("arpp-col-issued", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("arpp-sticky-left--ppp", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("arpp-sticky-left--reference", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ManageWorkspaceScript_ProvidesNavigationValidationAndFullscreenControls()
    {
        var script = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-manage.js");

        Assert.Contains("syncTableScrollbars", script, StringComparison.Ordinal);
        Assert.Contains("updateStickyMetrics", script, StringComparison.Ordinal);
        Assert.Contains("updateValidationNavigator", script, StringComparison.Ordinal);
        Assert.Contains("jumpToMatch", script, StringComparison.Ordinal);
        Assert.Contains("arpp-workspace-fullscreen", script, StringComparison.Ordinal);
        Assert.Contains("ResizeObserver", script, StringComparison.Ordinal);
        Assert.Contains("event.key.toLocaleLowerCase", script, StringComparison.Ordinal);
        Assert.Contains("form.requestSubmit", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsiveShell_UsesTheSelectedVariantAsTheActualWidthLimit()
    {
        var css = ReadRepoFile("wwwroot", "css", "site.css");

        Assert.Contains("--pm-page-shell-max: var(--pm-shell-standard)", css, StringComparison.Ordinal);
        Assert.Contains("var(--pm-page-shell-max)", css, StringComparison.Ordinal);
        Assert.Contains(".pm-page-shell--wide { --pm-page-shell-max: var(--pm-shell-wide); }", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".pm-page-shell--wide { max-width: var(--pm-shell-wide); }", css, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceCss_ReducesFrozenWidthAndSupportsFullscreenMode()
    {
        var css = ReadRepoFile("wwwroot", "css", "project-office-reports", "arpp-workspace.css");

        Assert.Contains(".arpp-entry-table {\n    min-width: 82rem;", css, StringComparison.Ordinal);
        Assert.Contains(".arpp-sticky-left--issued", css, StringComparison.Ordinal);
        Assert.Contains("body.arpp-workspace-fullscreen [data-arpp-workspace]", css, StringComparison.Ordinal);
        Assert.Contains(".arpp-table-scrollbar", css, StringComparison.Ordinal);
        Assert.Contains(".arpp-validation-navigator", css, StringComparison.Ordinal);
    }

    [Fact]
    public void UnlockDialog_UsesControlledInlineValidationWithoutBrowserBubble()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Details.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-details.js");

        Assert.Contains("data-arpp-unlock-counter", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-unlock-submit disabled", markup, StringComparison.Ordinal);
        Assert.Contains("syncUnlockReason", script, StringComparison.Ordinal);
        Assert.DoesNotContain("reportValidity", script, StringComparison.Ordinal);
        Assert.DoesNotContain("setCustomValidity", script, StringComparison.Ordinal);
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
