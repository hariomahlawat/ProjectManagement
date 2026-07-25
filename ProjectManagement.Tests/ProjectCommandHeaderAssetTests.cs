using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectCommandHeaderAssetTests
{
    [Fact]
    public void Overview_LoadsDedicatedHeaderSurfaceAfterPortfolioStyles()
    {
        var overview = ReadRepoFile("Pages", "Projects", "Overview.cshtml");
        const string portfolioAsset = "project-portfolio.css";
        const string headerAsset = "project-command-header.css";

        var portfolioIndex = overview.IndexOf(portfolioAsset, StringComparison.Ordinal);
        var headerIndex = overview.IndexOf(headerAsset, StringComparison.Ordinal);

        Assert.True(portfolioIndex >= 0, $"{portfolioAsset} is not referenced by the project overview.");
        Assert.True(headerIndex > portfolioIndex,
            $"{headerAsset} must be loaded after {portfolioAsset} so the critical surface remains authoritative.");
        Assert.Contains("asp-append-version=\"true\"", overview, StringComparison.Ordinal);
        Assert.Contains("data-project-command-header-stylesheet", overview, StringComparison.Ordinal);
    }

    [Fact]
    public void DedicatedHeaderStyles_PreserveActiveCompletedAndCancelledSurfaces()
    {
        var css = ReadRepoFile("wwwroot", "css", "pages", "project-command-header.css");

        Assert.Contains(".project-portfolio-shell > .project-command-header {", css, StringComparison.Ordinal);
        Assert.Contains("padding: .9rem 1rem;", css, StringComparison.Ordinal);
        Assert.Contains("border: 1px solid", css, StringComparison.Ordinal);
        Assert.Contains("border-left-width: 4px;", css, StringComparison.Ordinal);
        Assert.Contains("background-color: var(--pm-card, #fff);", css, StringComparison.Ordinal);
        Assert.Contains("overflow: visible;", css, StringComparison.Ordinal);
        Assert.Contains(".project-command-header--completed", css, StringComparison.Ordinal);
        Assert.Contains(".project-command-header--cancelled", css, StringComparison.Ordinal);
    }

    [Fact]
    public void PortfolioStyles_DoNotRedefineTheOuterHeaderSurface()
    {
        var css = ReadRepoFile("wwwroot", "css", "pages", "project-portfolio.css");
        var normalizedCss = css.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.DoesNotContain(".project-portfolio-shell > .project-command-header", css, StringComparison.Ordinal);
        Assert.DoesNotContain("\n.project-command-header {\n", normalizedCss, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Pages/Projects/Overview.cshtml")]
    [InlineData("Pages/Projects/_ProjectCommandHeader.cshtml")]
    [InlineData("wwwroot/css/pages/project-command-header.css")]
    [InlineData("ProjectManagement.Tests/ProjectCommandHeaderAssetTests.cs")]
    public void ReplacementManifest_IncludesHeaderDeliveryContract(string requiredPath)
    {
        var manifest = ReadRepoFile("REPLACEMENT-MANIFEST.txt");
        var entries = manifest.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Contains(requiredPath, entries);
    }

    [Fact]
    public void DevelopmentStaticFiles_DisableCachingForCssAndJavaScript()
    {
        var program = ReadRepoFile("Program.cs");

        Assert.Contains("StartsWithSegments(\"/js\"", program, StringComparison.Ordinal);
        Assert.Contains("StartsWithSegments(\"/css\"", program, StringComparison.Ordinal);
        Assert.Contains("NoStore = true", program, StringComparison.Ordinal);
        Assert.Contains("NoCache = true", program, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, Path.Combine(relativePath));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(relativePath)}");
    }
}
