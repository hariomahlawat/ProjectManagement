using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Services.Publications;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class BrochurePdfReportBuilderTests
{
    [Theory]
    [InlineData(BrochureCoverStyle.Institutional)]
    [InlineData(BrochureCoverStyle.Contemporary)]
    public void Build_GeneratesPdfForBothCoverStylesWithoutExternalAssets(BrochureCoverStyle coverStyle)
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"prism-brochure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(webRoot);
        try
        {
            var environment = new TestWebHostEnvironment(webRoot);
            var fontService = new FixedFontService();
            var builder = new BrochurePdfReportBuilder(environment, fontService);
            var data = BuildData(coverStyle);

            var bytes = builder.Build(data);

            Assert.True(bytes.Length > 5_000);
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public void SplitIntroduction_KeepsShortCopyOnOnePage()
    {
        const string text = "A concise institutional introduction for the capability publication.";

        var pages = BrochurePdfReportBuilder.SplitIntroduction(text, maximumWords: 330);

        var page = Assert.Single(pages);
        Assert.Equal(text, page);
    }

    [Fact]
    public void SplitIntroduction_BalancesLongCopyWithoutLosingWords()
    {
        var words = Enumerable.Range(1, 700).Select(index => $"word{index}").ToArray();
        var text = string.Join(" ", words);

        var pages = BrochurePdfReportBuilder.SplitIntroduction(text, maximumWords: 330);

        Assert.Equal(3, pages.Count);
        Assert.All(pages, page => Assert.InRange(BrochureLayoutPlanner.CountWords(page), 1, 330));

        var rebuilt = pages
            .SelectMany(page => page.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        Assert.Equal(words, rebuilt);
    }

    private static BrochurePublicationData BuildData(BrochureCoverStyle coverStyle)
    {
        var projects = Enumerable.Range(1, 5)
            .Select(id => new BrochurePublicationProject(
                id,
                $"Project {id}",
                "Other R&D Projects",
                "AR / VR",
                string.Join(" ", Enumerable.Range(1, 65).Select(index => $"word{index}")),
                65,
                PrimaryPhoto: null,
                SecondaryPhoto: null,
                BrochureImageMode.Automatic))
            .ToArray();

        var options = new BrochureBuildOptions(
            "SDD Capability Brochure",
            "Simulator Development Division",
            "Capability Edition · 2026",
            "Simulators of the Army, by the Army, for the Army",
            coverStyle,
            BrochureNarrativeSource.ProjectBrief,
            IntroductionTitle: null,
            IntroductionText: null,
            HandlingMarking: null,
            IssuerDisplayName: "Simulator Development Division",
            AllowTextOnlyProjects: true,
            GeneratedAtUtc: new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));

        return new BrochurePublicationData(
            options,
            projects,
            new BrochurePreflight(projects.Length, Array.Empty<BrochurePreflightIssue>()));
    }

    private sealed class FixedFontService : IPublicationFontService
    {
        private static readonly PublicationFontStatus Status = new(
            PublicationFontService.FallbackFamilyName,
            PublicationFontService.FallbackFamilyName,
            DmSansAvailable: false,
            AlatsiAvailable: false,
            MissingDmSansFiles: Array.Empty<string>(),
            SourceDescription: "Test fallback");

        public PublicationFontStatus EnsureRegistered() => Status;
        public PublicationFontStatus CurrentStatus => Status;
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment(string webRootPath)
        {
            WebRootPath = webRootPath;
            ContentRootPath = webRootPath;
            WebRootFileProvider = new PhysicalFileProvider(webRootPath);
            ContentRootFileProvider = WebRootFileProvider;
        }

        public string ApplicationName { get; set; } = "ProjectManagement.Tests";
        public IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
