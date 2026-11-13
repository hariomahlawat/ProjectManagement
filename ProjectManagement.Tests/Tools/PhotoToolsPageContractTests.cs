using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Pages.Tools.Photo;
using Xunit;

namespace ProjectManagement.Tests.Tools;

/// <summary>
/// Photo Tools changes users' own photos in the browser. The one exception is Remove background,
/// which sends a copy to the PRISM server's own model and gets back only a mask. These checks keep
/// the page signed-in only, free of page handlers that could receive photos, offline-safe, and
/// keep every other script off the network.
/// </summary>
public sealed class PhotoToolsPageContractTests
{
    private static readonly string[] Scripts =
    {
        Path.Combine("wwwroot", "js", "tools-shared", "zip-writer.js"),
        Path.Combine("wwwroot", "js", "photo-tools", "photo-tools-core.js"),
        Path.Combine("wwwroot", "js", "photo-tools", "photo-tools-canvas.js"),
        Path.Combine("wwwroot", "js", "photo-tools", "photo-tools-app.js"),
        Path.Combine("wwwroot", "js", "photo-tools", "photo-tools-passport.js"),
        Path.Combine("wwwroot", "js", "photo-tools", "photo-tools-print.js"),
        Path.Combine("wwwroot", "js", "photo-tools", "photo-tools-editor.js")
    };

    private static readonly string BackgroundScript =
        Path.Combine("wwwroot", "js", "photo-tools", "photo-tools-background.js");

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
    public void ToolCards_AreAnnouncedAsLinks()
    {
        // role="listitem" on a link hides that it is a link from screen readers.
        var root = FindProjectRoot();
        foreach (var tool in new[] { "Photo", "Pdf" })
        {
            var page = File.ReadAllText(Path.Combine(root, "Pages", "Tools", tool, "Index.cshtml"));
            Assert.DoesNotMatch(new Regex(@"<a\b[^>]*role=""listitem""", RegexOptions.IgnoreCase), page);
        }
    }

    [Fact]
    public void ChangingScreens_NamesTheTabAndMovesFocusToTheHeading()
    {
        var root = FindProjectRoot();
        var app = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "photo-tools", "photo-tools-app.js"));

        Assert.Contains("document.title", app, StringComparison.Ordinal);
        Assert.Contains("heading.focus(", app, StringComparison.Ordinal);
        Assert.Contains("showView(currentView(), { focus: false })", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_LoadsOnlyLocalAssetsAndNeverPostsPhotos()
    {
        var root = FindProjectRoot();
        var page = File.ReadAllText(Path.Combine(root, "Pages", "Tools", "Photo", "Index.cshtml"));

        Assert.DoesNotMatch(new Regex(@"(src|href)\s*=\s*""(https?:)?//", RegexOptions.IgnoreCase), page);
        Assert.DoesNotContain("<form", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Layout = null;", page, StringComparison.Ordinal);

        foreach (var script in Scripts)
        {
            var path = Path.Combine(root, script);
            Assert.True(File.Exists(path), $"{script} must ship with the application.");
            Assert.Contains(
                "~/" + script.Replace(Path.DirectorySeparatorChar, '/')["wwwroot/".Length..],
                page,
                StringComparison.Ordinal);

            var source = File.ReadAllText(path);
            Assert.DoesNotContain("fetch(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("XMLHttpRequest", source, StringComparison.Ordinal);
            Assert.DoesNotContain("sendBeacon", source, StringComparison.Ordinal);
            Assert.DoesNotContain("WebSocket", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RemoveBackground_TalksOnlyToPrismsOwnBackgroundEndpoints()
    {
        var root = FindProjectRoot();
        var page = File.ReadAllText(Path.Combine(root, "Pages", "Tools", "Photo", "Index.cshtml"));
        Assert.Contains("~/js/photo-tools/photo-tools-background.js", page, StringComparison.Ordinal);
        Assert.Contains("name=\"csrf-token\"", page, StringComparison.Ordinal);

        var source = File.ReadAllText(Path.Combine(root, BackgroundScript));
        Assert.Contains($"'{ProjectManagement.Features.PhotoTools.PhotoToolsApi.StatusPath}'", source, StringComparison.Ordinal);
        Assert.Contains($"'{ProjectManagement.Features.PhotoTools.PhotoToolsApi.MaskPath}'", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"https?://", RegexOptions.IgnoreCase), source);
        Assert.DoesNotContain("XMLHttpRequest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sendBeacon", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WebSocket", source, StringComparison.Ordinal);

        // Every fetch goes to one of the two endpoints, never to an address built at run time.
        var fetches = Regex.Matches(source, @"fetch\(([^,)]+)");
        Assert.Equal(2, fetches.Count);
        Assert.All(fetches, call => Assert.Contains(call.Groups[1].Value.Trim(), new[] { "STATUS_URL", "MASK_URL" }));
    }

    [Fact]
    public void Page_LoadsThePdfLibraryFromTheApplicationBeforePrintSheet()
    {
        var root = FindProjectRoot();
        var page = File.ReadAllText(Path.Combine(root, "Pages", "Tools", "Photo", "Index.cshtml"));

        Assert.True(
            File.Exists(Path.Combine(root, "wwwroot", "lib", "pdf-lib", "1.17.1", "dist", "pdf-lib.min.js")),
            "pdf-lib must ship with the application.");

        var library = page.IndexOf("~/lib/pdf-lib/1.17.1/dist/pdf-lib.min.js", StringComparison.Ordinal);
        var print = page.IndexOf("~/js/photo-tools/photo-tools-print.js", StringComparison.Ordinal);

        Assert.True(library >= 0 && library < print, "Print sheet needs pdf-lib loaded before photo-tools-print.js.");
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
