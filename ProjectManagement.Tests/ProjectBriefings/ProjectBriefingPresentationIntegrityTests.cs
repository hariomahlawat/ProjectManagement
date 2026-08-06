using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingPresentationIntegrityTests
{
    [Fact]
    public void Compose_SanitizesInvalidXmlCharactersAndReturnsSchemaValidDeck()
    {
        var composer = CreateComposer();
        var unpairedHighSurrogate = new string((char)0xD800, 1);
        var project = BuildProject(
            "Project" + '\u0001' + " Alpha" + unpairedHighSurrogate,
            "Progress" + '\u000B' + " recorded");
        var data = BuildData(project, "Integrity" + '\u0002' + " Review");

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(2, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        Assert.Empty(new OpenXmlValidator(FileFormatVersions.Office2021).Validate(document));

        var text = string.Join(
            "\n",
            document.PresentationPart!.SlideParts
                .SelectMany(slide => slide.Slide.Descendants<A.Text>())
                .Select(node => node.Text));
        Assert.DoesNotContain("\u0001", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\u000B", text, StringComparison.Ordinal);
        Assert.Contains("Project", text, StringComparison.Ordinal);
        Assert.Contains("Progress", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_UsesImageBinarySignatureInsteadOfUntrustedDeclaredContentType()
    {
        var composer = CreateComposer();
        var project = BuildProject("Image signature test", "Status recorded");
        project.CoverPhoto = TinyPng();
        project.CoverPhotoContentType = "image/jpeg";
        var data = BuildData(
            project,
            "Image signature test",
            ProjectBriefingPresentationMode.DetailedProjects);

        var (content, _) = composer.Compose(data);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        Assert.Empty(new OpenXmlValidator(FileFormatVersions.Office2021).Validate(document));
        var imageParts = document.PresentationPart!.SlideParts
            .SelectMany(slide => slide.ImageParts)
            .ToArray();
        var imagePart = Assert.Single(imageParts);
        Assert.Equal("image/png", imagePart.ContentType);
    }

    [Fact]
    public void Compose_RejectsTruncatedPngBeforeReturningPackage()
    {
        var composer = CreateComposer();
        var project = BuildProject("Truncated PNG test", "Status recorded");
        project.CoverPhoto = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        project.CoverPhotoContentType = "image/png";
        var data = BuildData(
            project,
            "Truncated PNG test",
            ProjectBriefingPresentationMode.DetailedProjects);

        var exception = Assert.Throws<ProjectBriefingPresentationIntegrityException>(() => composer.Compose(data));

        Assert.Contains("cannot be decoded", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_RejectsUnsupportedImagePayloadBeforeReturningPackage()
    {
        var composer = CreateComposer();
        var project = BuildProject("Unsupported image test", "Status recorded");
        project.CoverPhoto = Encoding.ASCII.GetBytes("RIFF0000WEBP");
        project.CoverPhotoContentType = "image/png";
        var data = BuildData(
            project,
            "Unsupported image test",
            ProjectBriefingPresentationMode.DetailedProjects);

        var exception = Assert.Throws<InvalidOperationException>(() => composer.Compose(data));

        Assert.Contains("not a supported PNG or JPEG payload", exception.Message, StringComparison.Ordinal);
    }

    private static ProjectBriefingSlideComposer CreateComposer()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "ProjectBriefing",
            "PresentationRoot");
        return new ProjectBriefingSlideComposer(new TestEnvironment(root));
    }

    private static ProjectBriefingPresentationData BuildData(
        ProjectBriefingPresentationProject project,
        string deckName,
        ProjectBriefingPresentationMode mode = ProjectBriefingPresentationMode.ExecutiveTable)
        => new()
        {
            DeckId = 9001,
            DeckName = deckName,
            PresentationMode = mode,
            CostMode = ProjectBriefingCostMode.None,
            NarrativeMode = ProjectBriefingNarrativeMode.CapabilityOverview,
            BrandingScope = ProjectBriefingBrandingScope.None,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 6, 5, 30, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = 1,
                OngoingCount = 1
            }
        };

    private static ProjectBriefingPresentationProject BuildProject(
        string projectName,
        string externalStatus)
        => new()
        {
            ProjectId = 9001,
            ProjectName = projectName,
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = "DEVP",
            PresentStage = "Development",
            PresentStageOrder = ProjectBriefingStageOrder.Development,
            ProjectCategory = "R&D",
            TechnicalCategory = "Simulation",
            CostRd = ProjectBriefingCostValue.Missing(),
            IpaCost = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.IPA),
            ProliferationCost = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation),
            ExternalStatus = externalStatus,
            BriefDescription = "A concise capability description for presentation integrity testing.",
            SortOrder = 1
        };

    private static byte[] TinyPng()
        => Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z1ZsAAAAASUVORK5CYII=");

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public TestEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            WebRootPath = Path.Combine(contentRootPath, "wwwroot");
            Directory.CreateDirectory(WebRootPath);
            ContentRootFileProvider = new NullFileProvider();
            WebRootFileProvider = new NullFileProvider();
        }

        public string ApplicationName { get; set; } = "ProjectManagement.Tests";
        public IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
