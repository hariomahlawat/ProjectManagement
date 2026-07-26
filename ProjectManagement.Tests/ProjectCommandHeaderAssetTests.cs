using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectCommandHeaderAssetTests
{
    [Fact]
    public void Overview_LoadsHeaderSurfaceThroughPrimaryPortfolioAsset()
    {
        var overview = ReadRepoFile("Pages", "Projects", "Overview.cshtml");
        const string portfolioAsset = "project-portfolio.css";

        Assert.Contains(portfolioAsset, overview, StringComparison.Ordinal);
        Assert.DoesNotContain("project-command-header.css", overview, StringComparison.Ordinal);
        Assert.Contains("asp-append-version=\"true\"", overview, StringComparison.Ordinal);
    }

    [Fact]
    public void PortfolioStyles_OwnRobustActiveCompletedAndCancelledHeaderSurfaces()
    {
        var css = ReadRepoFile("wwwroot", "css", "pages", "project-portfolio.css");

        Assert.Contains(
            ".project-portfolio .project-command-header[data-project-command-header] {",
            css,
            StringComparison.Ordinal);
        Assert.Contains("padding: .9rem 1rem;", css, StringComparison.Ordinal);
        Assert.Contains("border: 1px solid", css, StringComparison.Ordinal);
        Assert.Contains("border-left: 3px solid var(--project-command-accent);", css, StringComparison.Ordinal);
        Assert.Contains("var(--pm-card, #fff);", css, StringComparison.Ordinal);
        Assert.Contains("overflow: visible;", css, StringComparison.Ordinal);
        Assert.Contains(".project-command-header--completed[data-project-command-header]", css, StringComparison.Ordinal);
        Assert.Contains(".project-command-header--cancelled[data-project-command-header]", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".project-portfolio-shell > .project-command-header", css, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Pages/Projects/Overview.cshtml")]
    [InlineData("Pages/Projects/_ProjectCommandHeader.cshtml")]
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
