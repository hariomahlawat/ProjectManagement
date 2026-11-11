using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Pages.Tools.Photo;
using Xunit;

namespace ProjectManagement.Tests.Tools;

/// <summary>
/// Photo Tools changes users' own photos in the browser only. These checks keep the page
/// signed-in only, free of server handlers that could receive photos, and offline-safe.
/// </summary>
public sealed class PhotoToolsPageContractTests
{
    private static readonly string[] Scripts =
    {
        Path.Combine("wwwroot", "js", "tools-shared", "zip-writer.js"),
        Path.Combine("wwwroot", "js", "photo-tools", "photo-tools-core.js"),
        Path.Combine("wwwroot", "js", "photo-tools", "photo-tools-canvas.js"),
        Path.Combine("wwwroot", "js", "photo-tools", "photo-tools-app.js"),
        Path.Combine("wwwroot", "js", "photo-tools", "photo-tools-passport.js")
    };

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
