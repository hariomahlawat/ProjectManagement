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
    /// Every file of the tool, not only a list: each script in the folder (a new one is covered
    /// without editing this test), the shared ZIP writer, the stylesheet and the page and its
    /// partials. None may name an address on another site, open a network channel, or load code
    /// from anywhere but the addresses the page gives it.
    /// </summary>
    [Fact]
    public void EveryPdfToolsFile_StaysOffTheNetwork()
    {
        var root = FindProjectRoot();
        var scripts = Directory.GetFiles(Path.Combine(root, "wwwroot", "js", "pdf-tools"), "*.js")
            .Where(path => !path.EndsWith(".test.js", StringComparison.OrdinalIgnoreCase))
            .Append(Path.Combine(root, "wwwroot", "js", "tools-shared", "zip-writer.js"))
            .ToArray();
        var pages = Directory.GetFiles(Path.Combine(root, "Pages", "Tools", "Pdf"), "*.cshtml");
        var stylesheet = Path.Combine(root, "wwwroot", "css", "pdf-tools.css");
        Assert.True(scripts.Length >= 7, "The tool's scripts were not found.");

        var otherSite = new Regex(@"(https?:)?//[a-z0-9.-]+\.[a-z]{2,}", RegexOptions.IgnoreCase);
        foreach (var path in scripts.Concat(pages).Append(stylesheet))
        {
            var text = File.ReadAllText(path);
            var name = Path.GetFileName(path);
            Assert.False(otherSite.IsMatch(text), $"{name} names an address on another site.");
            foreach (var channel in new[] { "fetch(", "XMLHttpRequest", "sendBeacon", "WebSocket", "EventSource", "@import" })
            {
                Assert.False(text.Contains(channel, StringComparison.Ordinal), $"{name} uses {channel}.");
            }
        }

        // The stylesheet's url(...) values are local (no address, or one on this site).
        var css = File.ReadAllText(stylesheet);
        foreach (Match match in Regex.Matches(css, @"url\(\s*['""]?([^'"")\s]*)", RegexOptions.IgnoreCase))
        {
            var value = match.Groups[1].Value;
            Assert.True(value.Length == 0 || value.StartsWith("data:", StringComparison.Ordinal) || !value.Contains(':') && !value.StartsWith("//", StringComparison.Ordinal),
                $"pdf-tools.css loads {value}.");
        }

        // Code is loaded only from the addresses the page gives: import() of pdf.js, the Reduce
        // size worker, and the worker's importScripts of the page's own scripts. Those addresses
        // are this application's (Url.Content / the file version provider), never a literal URL.
        var allScripts = string.Join("\n", scripts.Select(File.ReadAllText));
        foreach (Match match in Regex.Matches(allScripts, @"\b(import|importScripts|new Worker)\s*\(\s*([^)]*)\)"))
        {
            var argument = match.Groups[2].Value.Trim();
            Assert.False(argument.StartsWith("'", StringComparison.Ordinal) || argument.StartsWith("\"", StringComparison.Ordinal) || argument.StartsWith("`", StringComparison.Ordinal),
                $"{match.Value} loads code from a literal address; it must come from the page.");
        }
        var page = File.ReadAllText(Path.Combine(root, "Pages", "Tools", "Pdf", "Index.cshtml"));
        // Each of the four addresses is written with Url.Content("~/...") or the file version
        // provider, so it is in this application whatever its path base.
        var addresses = Regex.Matches(page, @"data-(pdfjs-src|pdfjs-worker-src|pdfjs-cmap-url|compress-worker-src)=");
        var local = Regex.Matches(page, @"data-(pdfjs-src|pdfjs-worker-src|pdfjs-cmap-url|compress-worker-src)=""(@Url\.Content\(""~/|@FileVersions\.AddFileVersionToPath\(ViewContext\.HttpContext\.Request\.PathBase, ""/)");
        Assert.Equal(4, addresses.Count);
        Assert.Equal(4, local.Count);
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
