using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Pages.Tools.Collage;
using Xunit;

namespace ProjectManagement.Tests.Tools;

/// <summary>
/// Collage Studio (docs/ux/collage-studio.md) makes collages of users' own photos in the browser.
/// These checks keep the page signed-in only, free of page handlers that could receive photos,
/// offline-safe (everything it loads ships with PRISM), and its scripts off the network.
/// </summary>
public sealed class CollageStudioPageContractTests
{
    private static readonly string[] Scripts =
    {
        "js/collage/collage-core.js",
        "js/collage/collage-drafts.js",
        "js/collage/collage-draw.js",
        "js/collage/collage-app.js"
    };

    // Ways a script could reach a server.
    private static readonly string[] NetworkCalls =
    {
        "fetch(", "XMLHttpRequest", "sendBeacon", "WebSocket", "EventSource", "importScripts", "import(", "new Worker("
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
        var page = File.ReadAllText(Path.Combine(root, "Pages", "Tools", "Collage", "Index.cshtml"));

        Assert.DoesNotMatch(new Regex(@"(src|href)\s*=\s*[""'](https?:)?//", RegexOptions.IgnoreCase), page);
        Assert.DoesNotMatch(new Regex(@"url\(\s*[""']?(https?:)?//", RegexOptions.IgnoreCase), page);
        Assert.DoesNotContain("<form", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Layout = null;", page, StringComparison.Ordinal);

        // Every asset the page names ships with the application, Konva included.
        var assets = Regex.Matches(page, @"(?:src|href)\s*=\s*[""']~/([^""'?#]+)[""']")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Contains("lib/konva/10.9.1/konva.min.js", assets);
        // Save as PDF and "keep it under" use the PDF library and size search Photo Tools ships.
        Assert.Contains("lib/pdf-lib/1.17.1/dist/pdf-lib.min.js", assets);
        Assert.Contains("js/photo-tools/photo-tools-core.js", assets);
        foreach (var asset in assets)
        {
            Assert.True(File.Exists(Path.Combine(root, "wwwroot", asset)), $"~/{asset} must ship with the application.");
        }
        Assert.True(File.Exists(Path.Combine(root, "wwwroot", "lib", "konva", "10.9.1", "LICENSE")), "Konva's licence ships with it.");

        // Our own scripts make no network calls. Libraries under ~/lib/ are shipped as published.
        var scripts = Regex.Matches(page, @"<script\b[^>]*\bsrc\s*=\s*[""']~/([^""'?#]+)[""']", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value)
            .Where(script => !script.StartsWith("lib/", StringComparison.Ordinal))
            .ToArray();
        foreach (var expected in Scripts)
        {
            Assert.Contains(expected, scripts);
        }
        foreach (var script in scripts)
        {
            var source = File.ReadAllText(Path.Combine(root, "wwwroot", script));
            foreach (var call in NetworkCalls)
            {
                Assert.False(source.Contains(call, StringComparison.Ordinal), $"~/{script} must not use {call}");
            }
            Assert.DoesNotMatch(new Regex(@"https?://", RegexOptions.IgnoreCase), source);
        }

        // The page's own styles load nothing from elsewhere: no @import and no outside url().
        var styles = Regex.Matches(page, @"<link\b[^>]*rel=""stylesheet""[^>]*href=""~/(css/[^""?#]+)""", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Contains("css/collage.css", styles);
        foreach (var style in styles)
        {
            var css = File.ReadAllText(Path.Combine(root, "wwwroot", style));
            Assert.DoesNotContain("@import", css, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(new Regex(@"url\(\s*[""']?(https?:)?//", RegexOptions.IgnoreCase), css);
        }
    }

    [Fact]
    public void Page_AsksBeforeLosingWorkInThePrismDialog()
    {
        var root = FindProjectRoot();
        var page = File.ReadAllText(Path.Combine(root, "Pages", "Tools", "Collage", "Index.cshtml"));
        var app = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "collage", "collage-app.js"));

        Assert.Contains("<partial name=\"_PrismConfirmDialog\" />", page, StringComparison.Ordinal);
        Assert.Contains("window.PrismConfirm.show(options)", app, StringComparison.Ordinal);
        Assert.Contains("'beforeunload'", app, StringComparison.Ordinal);
    }

    [Fact]
    public void TextFonts_ShipWithPrism_EachWithItsLicence()
    {
        var root = FindProjectRoot();
        var page = File.ReadAllText(Path.Combine(root, "Pages", "Tools", "Collage", "Index.cshtml"));
        Assert.Contains("~/css/fonts/collage-fonts.css", page, StringComparison.Ordinal);

        // Every font file the stylesheet names is shipped, and only from PRISM itself.
        var css = File.ReadAllText(Path.Combine(root, "wwwroot", "css", "fonts", "collage-fonts.css"));
        var urls = Regex.Matches(css, @"url\('([^']+)'\)").Select(match => match.Groups[1].Value).ToArray();
        Assert.NotEmpty(urls);
        foreach (var url in urls)
        {
            Assert.StartsWith("../../fonts/collage/", url, StringComparison.Ordinal);
            var file = Path.GetFullPath(Path.Combine(root, "wwwroot", "css", "fonts", url));
            Assert.True(File.Exists(file), $"{url} must ship with the application.");
        }

        // Each font folder carries its SIL Open Font License.
        foreach (var folder in Directory.GetDirectories(Path.Combine(root, "wwwroot", "fonts", "collage")))
        {
            var licence = Path.Combine(folder, "OFL.txt");
            Assert.True(File.Exists(licence), $"{Path.GetFileName(folder)} must ship with OFL.txt.");
            Assert.Contains("SIL Open Font License", File.ReadAllText(licence), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Konva_IsListedInLibmanAtTheVersionThePageLoads()
    {
        var root = FindProjectRoot();
        var libman = File.ReadAllText(Path.Combine(root, "libman.json"));

        Assert.Contains("\"library\": \"konva@10.9.1\"", libman, StringComparison.Ordinal);
        Assert.Contains("\"destination\": \"wwwroot/lib/konva/10.9.1/\"", libman, StringComparison.Ordinal);
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
