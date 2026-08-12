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
    public void GenerateProjectCandidates_NormalFrontierNeverDropsBelowNinePoint()
    {
        using var fixture = new Fixture();
        var candidates = fixture.Service.GenerateProjectCandidates(Item(1, 190, BrochureImageMode.Single, hasSecondary: false));

        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate =>
            Assert.True(candidate.BodyFontSize >= BrochurePrintLayoutMetrics.ProjectBodyPreferredFontSize - .01f));
        Assert.Contains(candidates, candidate => candidate.Variant == BrochurePrintLayoutVariant.Dense);
        Assert.DoesNotContain(candidates, candidate => candidate.Variant == BrochurePrintLayoutVariant.Compact);
    }

    [Fact]
    public void GenerateProjectCandidates_UsesApprovedAdaptiveImageWindow()
    {
        using var fixture = new Fixture();
        var candidates = fixture.Service.GenerateProjectCandidates(Item(1, 170, BrochureImageMode.Single, hasSecondary: false));

        Assert.All(candidates.Where(candidate => candidate.ImageWidthPoints > 0f), candidate =>
            Assert.InRange(
                candidate.ImageWidthPoints,
                BrochurePrintLayoutMetrics.AdaptiveImageMinimumPoints,
                BrochurePrintLayoutMetrics.AdaptiveImageMaximumPoints));
        Assert.True(candidates.Select(candidate => candidate.ImageWidthPoints).Distinct().Count() > 1);
    }

    [Fact]
    public void GenerateProjectCandidates_ParetoFrontierIsBounded()
    {
        using var fixture = new Fixture();
        var candidates = fixture.Service.GenerateProjectCandidates(Item(1, 165, BrochureImageMode.Automatic, hasSecondary: true));

        Assert.InRange(candidates.Count, 1, BrochurePrintLayoutMetrics.MaximumParetoCandidatesPerProject);
    }

    [Fact]
    public void GenerateProjectCandidates_AutomaticMayUseSingleOrGallery()
    {
        using var fixture = new Fixture();
        var candidates = fixture.Service.GenerateProjectCandidates(Item(1, 145, BrochureImageMode.Automatic, hasSecondary: true));

        Assert.Contains(candidates, candidate => !candidate.UsesSecondaryImage);
        Assert.Contains(candidates, candidate => candidate.UsesSecondaryImage);
    }

    [Fact]
    public void GenerateProjectCandidates_ExplicitGalleryAlwaysUsesSecondImage()
    {
        using var fixture = new Fixture();
        var candidates = fixture.Service.GenerateProjectCandidates(Item(1, 145, BrochureImageMode.GalleryTwo, hasSecondary: true));

        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate => Assert.True(candidate.UsesSecondaryImage));
    }

    [Fact]
    public void MeasureProject_DenseCompactsGeometryWithoutReducingBodyFont()
    {
        using var fixture = new Fixture();
        var item = Item(1, 185, BrochureImageMode.Single, hasSecondary: false);
        var visual = fixture.Service.MeasureProject(item, BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Visual, 152f));
        var dense = fixture.Service.MeasureProject(item, BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Dense, 136f));

        Assert.Equal(BrochurePrintLayoutMetrics.ProjectBodyPreferredFontSize, visual.BodyFontSize);
        Assert.Equal(BrochurePrintLayoutMetrics.ProjectBodyPreferredFontSize, dense.BodyFontSize);
        Assert.True(dense.TotalHeightPoints < visual.TotalHeightPoints);
        Assert.True(dense.ParagraphSpacingPoints < visual.ParagraphSpacingPoints);
        Assert.True(dense.BodyPaddingPoints < visual.BodyPaddingPoints);
    }

    [Fact]
    public void MeasureProject_PublicationImageUsesExactSixteenByNineGeometry()
    {
        using var fixture = new Fixture();
        var measure = fixture.Service.MeasureProject(
            Item(1, 125, BrochureImageMode.Single, hasSecondary: false),
            BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Visual, 152f));

        Assert.InRange(
            Math.Abs((measure.ImageWidthPoints / BrochurePrintLayoutMetrics.SingleImageAspectRatio)
                     - measure.PrimaryImageHeightPoints),
            0f,
            .01f);
    }

    [Fact]
    public void MeasureProject_FloatSplitCarriesSemanticBoundaryClassification()
    {
        using var fixture = new Fixture();
        var narrative = string.Join("\n\n", new[]
        {
            "First sentence establishes the opening context and remains deliberately concise.",
            "Second sentence adds enough material to occupy the side column beside the publication photograph.",
            "Third sentence should normally continue below the photograph at full card width.",
            "Fourth sentence completes the representative project brief for deterministic testing."
        });
        var item = new BrochurePrintPlanningItem(
            1,
            "Sentence Boundary Project",
            narrative,
            BrochureImageMode.Single,
            HasPrimaryPhoto: true,
            HasSecondaryPhoto: false);

        var measure = fixture.Service.MeasureProject(
            item,
            BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Visual, 152f));

        Assert.NotEqual(BrochureFloatSplitKind.None, measure.FloatSplitKind);
        Assert.NotEmpty(measure.LeadingNarrative);
        var reconstructed = string.Join(
            " ",
            new[]
            {
                measure.LeadingNarrative,
                measure.ContinuationNarrative,
                measure.TrailingNarrative
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
        Assert.Equal(NormalizeNarrative(narrative), NormalizeNarrative(reconstructed));
    }

    [Fact]
    public void MeasureProject_RepeatedBlankLinesDoNotReserveFullTextLines()
    {
        using var fixture = new Fixture();
        var compactBreaks = new BrochurePrintPlanningItem(
            1,
            "Compact Paragraph Project",
            "First paragraph contains representative publication copy.\n\nSecond paragraph contains the same continuation copy.",
            BrochureImageMode.Single,
            HasPrimaryPhoto: true,
            HasSecondaryPhoto: false);
        var excessiveBreaks = compactBreaks with
        {
            Narrative = "First paragraph contains representative publication copy.\n\n\n\nSecond paragraph contains the same continuation copy."
        };
        var spec = BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Balanced, 148f);

        var normal = fixture.Service.MeasureProject(compactBreaks, spec);
        var repeated = fixture.Service.MeasureProject(excessiveBreaks, spec);

        Assert.InRange(Math.Abs(normal.TotalHeightPoints - repeated.TotalHeightPoints), 0f, .01f);
    }

    [Fact]
    public void MeasureProject_AerialDeliveryStyleParagraphsRemainACompleteSingleModule()
    {
        using var fixture = new Fixture();
        var narrative = string.Join("\n\n", new[]
        {
            "Aerial delivery training involves packing cargo parachutes and dropping supply loads such as ammunition ration and jerry can from various aircraft and helicopter platforms.",
            "Due to heavy cost resources involved and limited aircraft availability, live training is restricted to only a few drops during an annual calendar.",
            "A simulator provides representative training without imposing the same penalty on live resources while preserving safe procedure and realistic task sequence.",
            "The simulator is a more cost effective option for such training."
        });
        var item = new BrochurePrintPlanningItem(
            17,
            "VR-BASED AE DELIVERY SYSTEM FOR C17, C130 AND CHINOOK",
            narrative,
            BrochureImageMode.Single,
            HasPrimaryPhoto: true,
            HasSecondaryPhoto: false);

        var measurement = fixture.Service.MeasureProject(
            item,
            BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Dense, 136f));
        var reconstructed = string.Join(
            " ",
            new[]
            {
                measurement.LeadingNarrative,
                measurement.ContinuationNarrative,
                measurement.TrailingNarrative
            }.Where(part => !string.IsNullOrWhiteSpace(part)));

        Assert.Equal(NormalizeNarrative(narrative), NormalizeNarrative(reconstructed));
        Assert.True(measurement.TotalHeightPoints > measurement.TitleHeightPoints);
        Assert.Equal(BrochurePrintLayoutMetrics.ProjectBodyPreferredFontSize, measurement.BodyFontSize);
    }

    [Fact]
    public void MeasureProject_LongTitleGrowsBandWithoutReducingBodyTypography()
    {
        using var fixture = new Fixture();
        var item = new BrochurePrintPlanningItem(
            1,
            "Virtual Reality Based Observation Post End Training Simulator With Thermal Imager Integrated Observation Equipment",
            string.Join(" ", Enumerable.Range(1, 140).Select(index => $"word{index}")),
            BrochureImageMode.Single,
            HasPrimaryPhoto: true,
            HasSecondaryPhoto: false);

        var measure = fixture.Service.MeasureProject(
            item,
            BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Dense, 136f));

        Assert.Equal(BrochurePrintLayoutMetrics.ProjectBodyPreferredFontSize, measure.BodyFontSize);
        Assert.True(measure.TitleFontSize >= BrochurePrintLayoutMetrics.ProjectTitleMinimumFontSize);
        Assert.True(measure.TitleHeightPoints > measure.TitleMinimumHeightPoints);
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
        Assert.Equal(0f, plan.CentreBlockHeightPoints);
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

    private static string NormalizeNarrative(string value)
        => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
