using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Pages.Tools.Pdf;
using Xunit;

namespace ProjectManagement.Tests.Tools;

/// <summary>
/// PDF Tools edits users' own files in the browser only. These checks keep the page
/// signed-in only, free of server handlers that could receive files, and offline-safe.
/// </summary>
public sealed class PdfToolsPageContractTests
{
    [Fact]
    public void Page_RequiresASignedInUser()
    {
        Assert.NotNull(typeof(IndexModel).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(typeof(IndexModel).GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void Page_HasNoHandlersThatAcceptData()
    {
        var handlers = typeof(IndexModel)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .Where(name => name.StartsWith("On", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(new[] { "OnGet" }, handlers);
    }

    [Fact]
    public void Page_LoadsOnlyLocalAssetsAndNeverPostsFiles()
    {
        var root = FindProjectRoot();
        var page = File.ReadAllText(Path.Combine(root, "Pages", "Tools", "Pdf", "Index.cshtml"));
        var scriptNames = new[] { "pdf-tools-core.js", "pdf-tools-app.js", "pdf-tools-annotate.js", "pdf-tools-encode.js", "pdf-tools-compress.js" };
        // Reduce size runs in a background worker, which loads the page's own scripts by their
        // addresses on this site; it is checked like the page's scripts.
        const string workerName = "pdf-tools-compress-worker.js";
        var scripts = scriptNames.Append(workerName)
            .Select(name => File.ReadAllText(Path.Combine(root, "wwwroot", "js", "pdf-tools", name)))
            .ToArray();

        // Every script checked here is one the page loads, from the application itself.
        foreach (var name in scriptNames)
        {
            Assert.Contains($"src=\"~/js/pdf-tools/{name}\"", page, StringComparison.Ordinal);
        }
        Assert.Contains($"\"/js/pdf-tools/{workerName}\"", page, StringComparison.Ordinal);

        Assert.DoesNotMatch(new Regex(@"(src|href)\s*=\s*""(https?:)?//", RegexOptions.IgnoreCase), page);
        Assert.DoesNotContain("<form method=\"post\"", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Layout = null;", page, StringComparison.Ordinal);
        Assert.Contains("data-pdfjs-cmap-url", page, StringComparison.Ordinal);

        // Static files are served only for known extensions; pdf.js fetches .bcmap character maps.
        var program = File.ReadAllText(Path.Combine(root, "Program.cs"));
        Assert.Contains("contentTypeProvider.Mappings[\".bcmap\"]", program, StringComparison.Ordinal);

        foreach (var script in scripts)
        {
            Assert.DoesNotContain("fetch(", script, StringComparison.Ordinal);
            Assert.DoesNotContain("XMLHttpRequest", script, StringComparison.Ordinal);
            Assert.DoesNotContain("sendBeacon", script, StringComparison.Ordinal);
        }

        foreach (var asset in new[]
                 {
                     Path.Combine("wwwroot", "lib", "pdf-lib", "1.17.1", "dist", "pdf-lib.min.js"),
                     Path.Combine("wwwroot", "lib", "pdfjs", "4.10.38", "legacy", "build", "pdf.min.mjs"),
                     Path.Combine("wwwroot", "lib", "pdfjs", "4.10.38", "legacy", "build", "pdf.worker.min.mjs"),
                     Path.Combine("wwwroot", "lib", "pdfjs", "4.10.38", "cmaps", "UniJIS-UCS2-H.bcmap"),
                     Path.Combine("wwwroot", "lib", "pdfjs", "4.10.38", "cmaps", "UniGB-UCS2-H.bcmap")
                 })
        {
            Assert.True(File.Exists(Path.Combine(root, asset)), $"{asset} must ship with the application.");
        }
    }

    /// <summary>
    /// The page has its own shell (Layout = null), so it brings the PRISM confirmation dialog
    /// itself; without it every question (white-out, a large file, opening another PDF) would
    /// fall back to the browser's own confirm() box.
    /// </summary>
    [Fact]
    public void PdfTools_AsksInThePrismConfirmDialog()
    {
        var root = FindProjectRoot();
        var page = File.ReadAllText(Path.Combine(root, "Pages", "Tools", "Pdf", "Index.cshtml"));

        Assert.Contains("<partial name=\"_PrismConfirmDialog\" />", page, StringComparison.Ordinal);
        Assert.Contains("src=\"~/js/components/prism-confirm-dialog.js\"", page, StringComparison.Ordinal);
        Assert.Contains("href=\"~/css/components/prism-confirm-dialog.css\"", page, StringComparison.Ordinal);
        Assert.Contains("href=\"~/css/tools-shared/prism-confirm-standalone.css\"", page, StringComparison.Ordinal);
        // site.css is not loaded here, so every --pm-* token the dialog's stylesheet uses must be
        // given by the standalone stylesheet.
        var dialogCss = File.ReadAllText(Path.Combine(root, "wwwroot", "css", "components", "prism-confirm-dialog.css"));
        var standalone = File.ReadAllText(Path.Combine(root, "wwwroot", "css", "tools-shared", "prism-confirm-standalone.css"));
        var used = Regex.Matches(dialogCss, @"var\((--pm-[a-z0-9-]+)").Select(match => match.Groups[1].Value).Distinct();
        foreach (var token in used)
        {
            Assert.Contains($"{token}:", standalone, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PdfTools_LoadsTheSharedZipWriterBeforeItsCore()
    {
        var page = File.ReadAllText(Path.Combine(FindProjectRoot(), "Pages", "Tools", "Pdf", "Index.cshtml"));

        var zip = page.IndexOf("~/js/tools-shared/zip-writer.js", StringComparison.Ordinal);
        var core = page.IndexOf("~/js/pdf-tools/pdf-tools-core.js", StringComparison.Ordinal);

        Assert.True(zip >= 0 && zip < core, "PDF Tools needs the shared ZIP writer loaded before pdf-tools-core.js.");
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Project root could not be located.");
    }
}
