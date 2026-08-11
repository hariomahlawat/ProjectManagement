using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
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
            var builder = CreateBuilder(environment, fontService);
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
    public void Build_GeneratesDedicatedSingleFeatureWithIndependentCoverHero()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"prism-brochure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(webRoot);
        try
        {
            var environment = new TestWebHostEnvironment(webRoot);
            var fontService = new FixedFontService();
            var builder = CreateBuilder(environment, fontService);
            var pixel = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

            var primary = new BrochurePublicationImage(
                101,
                pixel,
                1920,
                1080,
                IsPrintReady: true,
                SourceVariant: "test-primary",
                Quality: BrochurePhotoQuality.Excellent);
            var hero = new BrochurePublicationImage(
                202,
                pixel,
                2400,
                1467,
                IsPrintReady: true,
                SourceVariant: "test-cover",
                Quality: BrochurePhotoQuality.Excellent);
            var narrative = string.Join(" ", Enumerable.Range(1, 195).Select(index => $"word{index}"));
            var project = new BrochurePublicationProject(
                1,
                "Single Feature Publication Project",
                "Other R&D Projects",
                "AR / VR",
                narrative,
                195,
                primary,
                SecondaryPhoto: null,
                BrochureImageMode.Single);
            var options = new BrochureBuildOptions(
                "SDD Capability Brochure",
                "Simulator Development Division",
                "Capability Edition · 2026",
                "Simulators of the Army, by the Army, for the Army",
                BrochureCoverStyle.Contemporary,
                BrochureNarrativeSource.ProjectBrief,
                BrochurePublicationProfile.DigitalComfortable,
                IntroductionTitle: null,
                IntroductionText: null,
                HandlingMarking: null,
                IssuerDisplayName: "Simulator Development Division",
                AllowTextOnlyProjects: false,
                GeneratedAtUtc: new DateTimeOffset(2026, 8, 11, 1, 0, 0, TimeSpan.Zero),
                CoverHeroProjectId: project.ProjectId,
                CoverHeroPhotoId: hero.PhotoId,
                CoverHeroFocalX: .5d,
                CoverHeroFocalY: .5d,
                IncludeBackCover: true);
            var data = new BrochurePublicationData(
                options,
                new[] { project },
                new BrochurePreflight(
                    1,
                    Array.Empty<BrochurePreflightIssue>(),
                    ResolvedCoverHeroProjectId: project.ProjectId,
                    ResolvedCoverHeroPhotoId: hero.PhotoId),
                hero);

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
    public void Build_PrintCompact_GeneratesReferenceFormatHardCopy()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"prism-brochure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(webRoot);
        try
        {
            var environment = new TestWebHostEnvironment(webRoot);
            var fontService = new FixedFontService();
            var builder = CreateBuilder(environment, fontService);
            var projects = Enumerable.Range(1, 6)
                .Select(id => new BrochurePublicationProject(
                    id,
                    $"Compact Print Project {id}",
                    "Other R&D Projects",
                    "Simulator",
                    string.Join(" ", Enumerable.Range(1, 135).Select(index => $"word{index}")),
                    135,
                    PrimaryPhoto: null,
                    SecondaryPhoto: null,
                    BrochureImageMode.Automatic))
                .ToArray();

            var options = new BrochureBuildOptions(
                "SDD Capability Brochure",
                "Simulator Development Division",
                "Capability Edition · 2026",
                "Simulators of the Army, by the Army, for the Army",
                BrochureCoverStyle.Institutional,
                BrochureNarrativeSource.ProjectBrief,
                BrochurePublicationProfile.PrintCompact,
                IntroductionTitle: null,
                IntroductionText: null,
                HandlingMarking: null,
                IssuerDisplayName: "Simulator Development Division",
                AllowTextOnlyProjects: true,
                GeneratedAtUtc: new DateTimeOffset(2026, 8, 11, 3, 0, 0, TimeSpan.Zero),
                PrintIntroText: "Institutional introduction.",
                PrintFutureText: "Future readiness narrative.",
                PrintProcurementText: "Procurement guidance.",
                PrintCentreStatement: "Centre of Expertise statement.",
                PrintDevelopingAgencyText: "Simulator Development Division.",
                PrintManufacturingAgencyText: "515 Army Base Workshop.",
                PrintVisionaryText: "Visionary horizons and strategic objectives.",
                PrintNewSimulatorsText: "New simulator requirements may be proposed through the prescribed channels.");

            var data = new BrochurePublicationData(
                options,
                projects,
                new BrochurePreflight(projects.Length, Array.Empty<BrochurePreflightIssue>()));

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

    private static BrochurePdfReportBuilder CreateBuilder(
        IWebHostEnvironment environment,
        IPublicationFontService fontService)
    {
        var measurement = new BrochurePrintMeasurementService(
            environment,
            fontService,
            NullLogger<BrochurePrintMeasurementService>.Instance);
        var planner = new BrochurePrintPagePlanner(measurement);
        return new BrochurePdfReportBuilder(environment, fontService, planner);
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
            BrochurePublicationProfile.DigitalComfortable,
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
