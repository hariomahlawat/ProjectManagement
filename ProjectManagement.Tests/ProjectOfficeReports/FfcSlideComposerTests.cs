using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.Ffc.Presentation;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcSlideComposerTests
{
    [Fact]
    public void Compose_CreatesOpenableWidescreenDeckWithExpectedSlidesAndText()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "Ffc", "PresentationRoot");
        var environment = new TestEnvironment(root);
        var composer = new FfcSlideComposer(new StubMapRenderer(), environment);

        var (content, slideCount) = composer.Compose(BuildData());

        Assert.True(content.Length > 10_000);
        Assert.Equal(9, slideCount);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var presentationPart = Assert.IsType<PresentationPart>(document.PresentationPart);
        var slideIds = presentationPart.Presentation.SlideIdList?.Elements<SlideId>().ToArray();
        Assert.NotNull(slideIds);
        Assert.Equal(slideCount, slideIds!.Length);
        Assert.Equal(12192000, presentationPart.Presentation.SlideSize?.Cx?.Value);
        Assert.Equal(6858000, presentationPart.Presentation.SlideSize?.Cy?.Value);

        var allSlideXml = string.Join(
            "\n",
            presentationPart.SlideParts.Select(part => part.Slide?.OuterXml ?? string.Empty));
        Assert.Contains("FFC GLOBAL PORTFOLIO", allSlideXml, StringComparison.Ordinal);
        Assert.Contains("Portfolio at a glance", allSlideXml, StringComparison.Ordinal);
        Assert.Contains("Global footprint", allSlideXml, StringComparison.Ordinal);
        Assert.Contains("Myanmar", allSlideXml, StringComparison.Ordinal);
    }

    private static FfcPresentationData BuildData()
    {
        var record = new FfcPresentationRecord(
            RecordId: 9,
            Year: 2025,
            ProjectCount: 1,
            IpaCompleted: true,
            IpaDate: new DateOnly(2025, 9, 8),
            IpaRemarks: null,
            GslCompleted: true,
            GslDate: new DateOnly(2025, 9, 8),
            GslRemarks: null,
            OverallPosition: "Delivery and installation progressing.",
            InstalledUnits: 0,
            DeliveredNotInstalledUnits: 0,
            PlannedUnits: 1,
            UpdatedAt: new DateTimeOffset(2026, 7, 19, 10, 0, 0, TimeSpan.Zero),
            Projects: new[]
            {
                new FfcPresentationProject(
                    1,
                    101,
                    "Indigenous Swarm Drones Algorithm",
                    "Swarm drones",
                    1,
                    FfcUnitPosition.Planned,
                    "Commercial Bid Opening",
                    "Technical evaluation completed.")
            },
            Attachments: Array.Empty<FfcPresentationAttachment>());

        var country = new FfcPresentationCountry(
            1,
            "Myanmar",
            "MMR",
            1,
            1,
            0,
            0,
            1,
            record.UpdatedAt,
            new[] { record });

        return new FfcPresentationData(
            "FFC Global Portfolio",
            "Position as at 19 Jul 2026",
            "OFFICIAL",
            record.UpdatedAt,
            FfcPresentationType.FullPortfolio,
            IncludeProjects: true,
            IncludeProgress: true,
            IncludeMilestoneRemarks: false,
            IncludeAttachmentRegister: false,
            new FfcFootprintSummary(1, 1, 1, 0, 0, 1),
            new[] { country });
    }

    private sealed class StubMapRenderer : IFfcPresentationMapRenderer
    {
        private static readonly byte[] Png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZV3sAAAAASUVORK5CYII=");

        public byte[] Render(
            IReadOnlyList<FfcPresentationCountry> countries,
            int width = 1800,
            int height = 1180)
            => Png;
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public TestEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            WebRootPath = Path.Combine(contentRootPath, "wwwroot");
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
