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
        Assert.Contains("TOTAL QUANTITY", allSlideXml, StringComparison.Ordinal);
        Assert.Contains("QUANTITY STATUS", allSlideXml, StringComparison.Ordinal);
        Assert.Contains("Country-wise quantity status", allSlideXml, StringComparison.Ordinal);
        Assert.Contains("IPA and GSL status", allSlideXml, StringComparison.Ordinal);
        Assert.Contains("OVERALL STATUS", allSlideXml, StringComparison.Ordinal);
        Assert.DoesNotContain("TOTAL UNITS", allSlideXml, StringComparison.Ordinal);
        Assert.DoesNotContain("UNIT POSITION", allSlideXml, StringComparison.Ordinal);
        Assert.DoesNotContain("OVERALL POSITION", allSlideXml, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_GlobalFootprintListsAllElevenCountriesWithoutPerRowQtySuffix()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "Ffc", "PresentationRoot");
        var composer = new FfcSlideComposer(new StubMapRenderer(), new TestEnvironment(root));

        var (content, _) = composer.Compose(BuildGlobalFootprintData());

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var presentationPart = Assert.IsType<PresentationPart>(document.PresentationPart);
        var slideIds = Assert.IsType<SlideIdList>(presentationPart.Presentation.SlideIdList)
            .Elements<SlideId>()
            .ToArray();
        var footprintSlide = Assert.IsType<SlidePart>(
            presentationPart.GetPartById(slideIds[2].RelationshipId!.Value!));
        var xml = footprintSlide.Slide?.OuterXml ?? string.Empty;

        foreach (var expected in new[]
                 {
                     "Ethiopia", "Myanmar", "Sri Lanka", "Bangladesh", "Namibia",
                     "Nepal", "Nigeria", "Cambodia", "France", "Mozambique", "Tanzania"
                 })
        {
            Assert.Contains(expected, xml, StringComparison.Ordinal);
        }

        Assert.Contains("COUNTRY", xml, StringComparison.Ordinal);
        Assert.Contains("QTY", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("+ 2 more countries", xml, StringComparison.Ordinal);
        Assert.DoesNotContain(">Qty<", xml, StringComparison.Ordinal);
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
            "Status as at 19 Jul 2026",
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

    private static FfcPresentationData BuildGlobalFootprintData()
    {
        var now = new DateTimeOffset(2026, 7, 19, 10, 0, 0, TimeSpan.Zero);
        var source = new[]
        {
            (1L, "Ethiopia", "ETH", 13),
            (2L, "Myanmar", "MMR", 10),
            (3L, "Sri Lanka", "LKA", 7),
            (4L, "Bangladesh", "BGD", 3),
            (5L, "Namibia", "NAM", 2),
            (6L, "Nepal", "NPL", 2),
            (7L, "Nigeria", "NGA", 2),
            (8L, "Cambodia", "KHM", 1),
            (9L, "France", "FRA", 1),
            (10L, "Mozambique", "MOZ", 1),
            (11L, "Tanzania, United Republic of", "TZA", 1)
        };
        var countries = source
            .Select(item => new FfcPresentationCountry(
                item.Item1,
                item.Item2,
                item.Item3,
                1,
                1,
                0,
                0,
                item.Item4,
                now,
                Array.Empty<FfcPresentationRecord>()))
            .ToArray();

        return new FfcPresentationData(
            "FFC Global Portfolio",
            "Position as at 19 Jul 2026",
            null,
            now,
            FfcPresentationType.ExecutiveBrief,
            IncludeProjects: false,
            IncludeProgress: false,
            IncludeMilestoneRemarks: false,
            IncludeAttachmentRegister: false,
            new FfcFootprintSummary(11, 11, 11, 0, 0, 43),
            countries);
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
