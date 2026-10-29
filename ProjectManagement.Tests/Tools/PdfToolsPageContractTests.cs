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
        var scripts = new[] { "pdf-tools-core.js", "pdf-tools-app.js", "pdf-tools-annotate.js" }
            .Select(name => File.ReadAllText(Path.Combine(root, "wwwroot", "js", "pdf-tools", name)))
            .ToArray();

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
