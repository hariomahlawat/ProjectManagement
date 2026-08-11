using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Services.Publications;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class BrochurePrintMeasurementServiceTests
{
    [Fact]
    public void MeasureProject_LongerNarrativeRequiresMoreHeight()
    {
        using var fixture = new Fixture();
        var shortItem = Item(1, 80, BrochureImageMode.Single, hasSecondary: false);
        var longItem = Item(2, 170, BrochureImageMode.Single, hasSecondary: false);

        var shortMeasure = fixture.Service.MeasureProject(shortItem, BrochurePrintLayoutVariant.Balanced);
        var longMeasure = fixture.Service.MeasureProject(longItem, BrochurePrintLayoutVariant.Balanced);

        Assert.True(longMeasure.TotalHeightPoints > shortMeasure.TotalHeightPoints);
    }

    [Fact]
    public void MeasureProject_GalleryTwoRequiresMoreHeightThanSingle()
    {
        using var fixture = new Fixture();
        var single = Item(1, 90, BrochureImageMode.Single, hasSecondary: true);
        var gallery = single with { ImageMode = BrochureImageMode.GalleryTwo };

        var singleMeasure = fixture.Service.MeasureProject(single, BrochurePrintLayoutVariant.Balanced);
        var galleryMeasure = fixture.Service.MeasureProject(gallery, BrochurePrintLayoutVariant.Balanced);

        Assert.True(galleryMeasure.TotalHeightPoints > singleMeasure.TotalHeightPoints);
    }

    [Fact]
    public void MeasureFrontPage_ApprovedReferenceNeverDropsBelowTypographyFloor()
    {
        using var fixture = new Fixture();

        var plan = fixture.Service.MeasureFrontPage(
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            "Simulators of the Army, by the Army, for the Army");

        Assert.True(plan.BodyFontSize >= BrochurePrintLayoutMetrics.FrontBodyMinimumFontSize);
        Assert.True(plan.HeroHeightPoints >= BrochurePrintLayoutMetrics.FrontMinimumHeroHeightPoints);
        Assert.InRange(plan.UtilizationPercent, 90, 100);
    }

    private static BrochurePrintPlanningItem Item(
        int id,
        int words,
        BrochureImageMode mode,
        bool hasSecondary)
        => new(
            id,
            $"Measured Project {id}",
            string.Join(" ", Enumerable.Range(1, words).Select(index => $"word{index}")),
            mode,
            HasPrimaryPhoto: true,
            HasSecondaryPhoto: hasSecondary);

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;

        public Fixture()
        {
            _root = Path.Combine(Path.GetTempPath(), $"prism-print-measure-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);
            var environment = new TestWebHostEnvironment(_root);
            Service = new BrochurePrintMeasurementService(
                environment,
                new FixedFontService(),
                NullLogger<BrochurePrintMeasurementService>.Instance);
        }

        public BrochurePrintMeasurementService Service { get; }

        public void Dispose()
        {
            Service.Dispose();
            Directory.Delete(_root, recursive: true);
        }
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
