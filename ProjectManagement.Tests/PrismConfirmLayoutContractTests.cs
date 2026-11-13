using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// One confirmation dialog for the whole app (docs/ux/confirm-dialogs.md): _Layout renders it
/// once with its script and styles, and no page renders a second copy.
/// </summary>
public sealed class PrismConfirmLayoutContractTests
{
    [Fact]
    public void Layout_RendersTheConfirmDialogWithItsScriptAndStyles()
    {
        var layout = ReadRepoFile("Pages", "Shared", "_Layout.cshtml");

        Assert.Contains("<partial name=\"_PrismConfirmDialog\" />", layout, StringComparison.Ordinal);
        Assert.Contains("~/js/components/prism-confirm-dialog.js", layout, StringComparison.Ordinal);
        Assert.Contains("~/css/components/prism-confirm-dialog.css", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void NoPage_RendersASecondConfirmDialog()
    {
        var root = ResolveRepoRoot();
        var offenders = new[] { "Pages", "Areas" }
            .Select(dir => Path.Combine(root, dir))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cshtml", SearchOption.AllDirectories))
            .Where(path => !path.EndsWith(Path.Combine("Shared", "_Layout.cshtml"), StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("_PrismConfirmDialog", StringComparison.Ordinal)
                && !path.EndsWith("_PrismConfirmDialog.cshtml", StringComparison.OrdinalIgnoreCase))
            // A standalone page with its own shell (Layout = null, such as PDF Tools) does not
            // get the layout's dialog, so it renders the one dialog itself.
            .Where(path => !File.ReadAllText(path).Contains("Layout = null;", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Dialog_HonoursAccessibilityPreferences()
    {
        var css = ReadRepoFile("wwwroot", "css", "components", "prism-confirm-dialog.css");

        Assert.Contains("::backdrop", css, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", css, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-transparency", css, StringComparison.Ordinal);
        // Colour literals are policed by tools/theme-coverage.js (npm test); this file has none.
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
