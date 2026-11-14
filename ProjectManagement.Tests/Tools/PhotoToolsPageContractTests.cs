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

    // Ways a script could reach a server. Remove background is checked on its own, below.
    private static readonly string[] NetworkCalls =
    {
        "fetch(", "XMLHttpRequest", "sendBeacon", "WebSocket", "EventSource", "importScripts", "import(", "new Worker("
    };

    [Fact]
    public void Page_LoadsOnlyLocalAssetsAndNeverPostsPhotos()
    {
        var root = FindProjectRoot();
        var page = File.ReadAllText(Path.Combine(root, "Pages", "Tools", "Photo", "Index.cshtml"));

        Assert.DoesNotMatch(new Regex(@"(src|href)\s*=\s*[""'](https?:)?//", RegexOptions.IgnoreCase), page);
        Assert.DoesNotMatch(new Regex(@"url\(\s*[""']?(https?:)?//", RegexOptions.IgnoreCase), page);
        Assert.DoesNotContain("<form", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Layout = null;", page, StringComparison.Ordinal);

        // Every asset the page names ships with the application.
        var assets = Regex.Matches(page, @"(?:src|href)\s*=\s*[""']~/([^""'?#]+)[""']")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.NotEmpty(assets);
        foreach (var asset in assets)
        {
            Assert.True(File.Exists(Path.Combine(root, "wwwroot", asset)), $"~/{asset} must ship with the application.");
        }

        // The scripts are read from the page itself, so a script added later is checked too.
        // Libraries under ~/lib/ are shipped as published and are not ours to scan.
        var scripts = Regex.Matches(page, @"<script\b[^>]*\bsrc\s*=\s*[""']~/([^""'?#]+)[""']", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value)
            .Where(script => !script.StartsWith("lib/", StringComparison.Ordinal))
            .Where(script => script != BackgroundScript.Replace(Path.DirectorySeparatorChar, '/')["wwwroot/".Length..])
            .ToArray();
        foreach (var expected in Scripts)
        {
            Assert.Contains(expected.Replace(Path.DirectorySeparatorChar, '/')["wwwroot/".Length..], scripts);
        }
        foreach (var script in scripts)
        {
            var source = File.ReadAllText(Path.Combine(root, "wwwroot", script));
            foreach (var call in NetworkCalls)
            {
                Assert.False(source.Contains(call, StringComparison.Ordinal), $"~/{script} must not use {call}");
            }
        }

        // The page's own styles load nothing from elsewhere: no @import and no outside url().
        var styles = Regex.Matches(page, @"<link\b[^>]*rel=""stylesheet""[^>]*href=""~/(css/[^""?#]+)""", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Contains("css/photo-tools.css", styles);
        Assert.Contains("css/pdf-tools.css", styles);
        foreach (var style in styles)
        {
            var css = File.ReadAllText(Path.Combine(root, "wwwroot", style));
            Assert.DoesNotContain("@import", css, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(new Regex(@"url\(\s*[""']?(https?:)?//", RegexOptions.IgnoreCase), css);
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
        Assert.Contains($"'{ProjectManagement.Features.PhotoTools.PhotoToolsApi.FacePath}'", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"https?://", RegexOptions.IgnoreCase), source);
        Assert.DoesNotContain("XMLHttpRequest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sendBeacon", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WebSocket", source, StringComparison.Ordinal);

        // Every fetch goes to one of PRISM's three endpoints, never to an address built at run time.
        // Passport photo asks where the face is through this script (window.PrismPhotoServer).
        var fetches = Regex.Matches(source, @"fetch\(([^,)]+)");
        Assert.Equal(3, fetches.Count);
        Assert.All(fetches, call => Assert.Contains(call.Groups[1].Value.Trim(), new[] { "STATUS_URL", "MASK_URL", "FACE_URL" }));
    }

    [Fact]
    public void PassportPhoto_AsksForTheModelOnlyThroughRemoveBackground_AndOffersBetterEdges()
    {
        var root = FindProjectRoot();
        var page = File.ReadAllText(Path.Combine(root, "Pages", "Tools", "Photo", "Index.cshtml"));
        var passport = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "photo-tools", "photo-tools-passport.js"));
        var background = File.ReadAllText(Path.Combine(root, BackgroundScript));

        // The one script that talks to the server lends Passport photo its mask request.
        Assert.Contains("mask: maskFor", background, StringComparison.Ordinal);
        Assert.Contains("window.PrismPhotoServer.mask(", passport, StringComparison.Ordinal);
        Assert.All(NetworkCalls, call => Assert.DoesNotContain(call, passport, StringComparison.Ordinal));

        // Better edges, the wait with Cancel, and Try again are on the page; the privacy line says
        // a copy goes to the PRISM server and is not stored.
        Assert.Contains("data-action=\"bg-model\"", page, StringComparison.Ordinal);
        Assert.Contains("data-action=\"bg-model-cancel\"", page, StringComparison.Ordinal);
        Assert.Contains("data-action=\"bg-model-retry\"", page, StringComparison.Ordinal);
        Assert.Contains("used in memory and not stored", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_SaysWhichScreensSendACopyToThePrismServer()
    {
        var root = FindProjectRoot();
        var page = File.ReadAllText(Path.Combine(root, "Pages", "Tools", "Photo", "Index.cshtml"));

        // Passport photo asks the server where the face is and, when needed, for the mask, so the
        // page must never again say that only Remove background sends a copy.
        Assert.DoesNotContain("Only Remove background", page, StringComparison.Ordinal);
        Assert.Contains("Two screens send a copy to the PRISM server: Remove background", page, StringComparison.Ordinal);
        Assert.Contains("except Remove background and Passport photo", page, StringComparison.Ordinal);
        Assert.Contains("To place the face, and for a new background when the wall is not plain, a copy of the photo is sent to the PRISM server", page, StringComparison.Ordinal);
        Assert.Contains("Better edges sends a copy of the photo to the PRISM server", page, StringComparison.Ordinal);
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
    public void Signature_HasItsOwnCardThatOpensPassportPhotoInSignatureMode()
    {
        var root = FindProjectRoot();
        var page = File.ReadAllText(Path.Combine(root, "Pages", "Tools", "Photo", "Index.cshtml"));
        var app = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "photo-tools", "photo-tools-app.js"));
        var passport = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "photo-tools", "photo-tools-passport.js"));

        Assert.Contains("href=\"#signature\"", page, StringComparison.Ordinal);
        Assert.Contains("signature: 'passport'", app, StringComparison.Ordinal);
        Assert.Contains("'#signature': 'signature'", passport, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveBackground_SaysWhenItIsOff_AndKeepsThePhotoWhenAStepStops()
    {
        // Owner decision (14 Nov 2026): when the tool is off, the home card is dimmed and says
        // "Not set up on this server". A busy, slow or cancelled step keeps the photo with Try again.
        var root = FindProjectRoot();
        var page = File.ReadAllText(Path.Combine(root, "Pages", "Tools", "Photo", "Index.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "photo-tools", "photo-tools-background.js"));

        Assert.Contains("data-remove-card", page, StringComparison.Ordinal);
        Assert.Contains(">Not set up on this server<", page, StringComparison.Ordinal);
        Assert.Contains("data-action=\"remove-retry\"", page, StringComparison.Ordinal);
        Assert.Contains("data-action=\"remove-cancel\"", page, StringComparison.Ordinal);
        Assert.Contains("data-remove-ready-only", page, StringComparison.Ordinal);
        Assert.Contains("showRetry(", script, StringComparison.Ordinal);
        Assert.Contains("response.status === 401", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveBackground_SaveAndBrush_StaySafeAndLight()
    {
        // PR 6 of docs/ux/photo-tools-remove-background-fixes.md.
        var root = FindProjectRoot();
        var script = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "photo-tools", "photo-tools-background.js"));

        // The save takes what it needs first and leaves a newer photo's screen alone.
        Assert.Contains("const job = {", script, StringComparison.Ordinal);
        Assert.Contains("job.run === openRun", script, StringComparison.Ordinal);
        // It does not wait for an animation frame, which a hidden tab never gets.
        Assert.DoesNotContain("requestAnimationFrame(() => setTimeout", script, StringComparison.Ordinal);
        // A mask request has a browser-side limit. Looking at another tool on the page does not
        // stop it (the result is there on coming back); its error waits for this screen.
        Assert.Contains("MASK_GIVE_UP_MS", script, StringComparison.Ordinal);
        Assert.DoesNotContain("hash !== '#remove' && maskPending", script, StringComparison.Ordinal);
        Assert.Contains("showErrorHere(", script, StringComparison.Ordinal);
        Assert.Contains("errorWhenBack", script, StringComparison.Ordinal);
        // A stroke ends when its pointer is lost.
        Assert.Contains("'lostpointercapture'", script, StringComparison.Ordinal);
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
