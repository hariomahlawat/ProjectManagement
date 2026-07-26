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
        var header = ReadRepoFile("Pages", "Projects", "_ProjectCommandHeader.cshtml");
        var css = ReadRepoFile("wwwroot", "css", "pages", "project-portfolio.css");

        Assert.Contains("class=\"card pm-card project-command-header @lifecycleHeaderClass\"", header, StringComparison.Ordinal);
        Assert.Contains("data-project-command-header=\"true\"", header, StringComparison.Ordinal);
        Assert.Contains(".project-portfolio .project-command-header {", css, StringComparison.Ordinal);
        Assert.Contains(".project-command-header__main {", css, StringComparison.Ordinal);
        Assert.Contains("padding: .9rem 1rem .8rem;", css, StringComparison.Ordinal);
        Assert.Contains("padding: .85rem 1rem 1rem;", css, StringComparison.Ordinal);
        Assert.Contains("border: 1px solid #c7d0dd;", css, StringComparison.Ordinal);
        Assert.Contains("border-left: 3px solid var(--project-command-accent);", css, StringComparison.Ordinal);
        Assert.Contains("background-color: #fff;", css, StringComparison.Ordinal);
        Assert.Contains("var(--pm-card, #fff);", css, StringComparison.Ordinal);
        Assert.Contains("overflow: visible;", css, StringComparison.Ordinal);
        Assert.Contains(".project-portfolio .project-command-header--completed {", css, StringComparison.Ordinal);
        Assert.Contains(".project-portfolio .project-command-header--cancelled {", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".project-command-header[data-project-command-header]", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".project-portfolio-shell > .project-command-header", css, StringComparison.Ordinal);
    }

    [Fact]
    public void PortfolioStyles_ApplyHeaderSurfaceAfterGenericCardContract()
    {
        var css = ReadRepoFile("wwwroot", "css", "pages", "project-portfolio.css");
        var genericCardIndex = css.IndexOf(".project-portfolio .pm-card {", StringComparison.Ordinal);
        var headerSurfaceIndex = css.IndexOf(".project-portfolio .project-command-header {", StringComparison.Ordinal);
        var completedSurfaceIndex = css.IndexOf(".project-portfolio .project-command-header--completed {", StringComparison.Ordinal);
        var cancelledSurfaceIndex = css.IndexOf(".project-portfolio .project-command-header--cancelled {", StringComparison.Ordinal);

        Assert.True(genericCardIndex >= 0, "The generic portfolio card contract was not found.");
        Assert.True(headerSurfaceIndex > genericCardIndex, "The command header must override the generic card contract.");
        Assert.True(completedSurfaceIndex > headerSurfaceIndex, "The completed lifecycle surface must override the base header.");
        Assert.True(cancelledSurfaceIndex > headerSurfaceIndex, "The cancelled lifecycle surface must override the base header.");
    }

    [Theory]
    [InlineData("Pages/Projects/Overview.cshtml")]
    [InlineData("Pages/Projects/_ProjectCommandHeader.cshtml")]
    [InlineData("wwwroot/css/pages/project-portfolio.css")]
    [InlineData("ProjectManagement.Tests/ProjectCommandHeaderAssetTests.cs")]
    [InlineData("ProjectManagement.Tests/ProjectOverviewPresentationContractTests.cs")]
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
