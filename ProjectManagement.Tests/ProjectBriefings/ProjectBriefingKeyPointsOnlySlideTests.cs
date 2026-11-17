using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using ProjectManagement.Services.Projects;
using SkiaSharp;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace ProjectManagement.Tests.ProjectBriefings;

// The "Key points only" project slide (docs/ux/briefing-decks.md, blocks plan PR 5). Most tests
// compose the slide alone through the composer's internal test path; one composes a whole deck set
// to the design.
public sealed class ProjectBriefingKeyPointsOnlySlideTests
{
    [Fact]
    public void ADeckSetToKeyPointsOnly_GivesEachProjectOneSuchSlide_AndNoCapabilityOrBriefSlides()
    {
        var data = Deck(Project());
        data = new ProjectBriefingPresentationData
        {
            DeckId = data.DeckId,
            DeckName = data.DeckName,
            PresentationMode = data.PresentationMode,
            PresentationTheme = data.PresentationTheme,
            CostMode = data.CostMode,
            NarrativeMode = ProjectBriefingNarrativeMode.CapabilityOverview,
            StandardSlideOptions = data.StandardSlideOptions with
            {
                KeyPoints = ProjectBriefingKeyPointPlacement.CapabilitySlide,
                KeyPointsOnly = true
            },
            GeneratedAtUtc = data.GeneratedAtUtc,
            Projects = data.Projects,
            Summary = data.Summary
        };

        var composition = new ProjectBriefingSlideComposer(new TestEnvironment(
                Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot")))
            .ComposeWithOutline(data);

        var projectSlides = composition.Outline.Where(entry => entry.ProjectId == 901).ToArray();
        Assert.Equal(ProjectBriefingSlideRole.ProjectKeyPointsOnly, Assert.Single(projectSlides).Role);
        Assert.DoesNotContain(composition.Outline, entry => entry.Role
            is ProjectBriefingSlideRole.ProjectCapability or ProjectBriefingSlideRole.ProjectBrief);
        Assert.Equal(ProjectBriefingSlideRole.ProjectKeyPointsOnly, ProjectBriefingSlideComposer.KeyPointSlide(data));
    }

    private const double Emu = 914400d;

    // Recorded key points of a local demo project (VR Bailey Bridge Sml).
    private static readonly string[] RealisticPoints =
    {
        "Live practice of Bailey bridge layout and construction drills is costly, limited and hard to repeat.",
        "Engineer field companies must be proficient in Bailey bridge layout and construction drills before deployment.",
        "Every bridge panel, transom and pin modelled to drawing.",
        "Guided lessons, instant feedback and an instructor station.",
        "Can be fielded at schools and units with little change."
    };

    // Five points at the 130-character limit, with long words so they wrap as badly as real text.
    private static readonly string[] LongestPoints =
    {
        "Live practice of armoured recovery, towing and winching of disabled tanks under fire uses scarce equipment, ranges and fuel today.",
        "Recovery crews of armoured regiments must be fully proficient in battlefield recovery drills before induction into formations.",
        "Replica crane, winch and hydraulic controls with VR views of the recovery site, including slopes, ditches, mud and night scenes.",
        "Step-by-step guided lessons, instant feedback after every drill and an instructor station that records and replays every run.",
        "Can be fielded at the Armoured Corps Centre, EME schools and every regiment's training area with very little change to the design."
    };

    [Fact]
    public void LongestRealisticPoints_AreAtTheCharacterLimit()
        => Assert.All(LongestPoints, point => Assert.InRange(point.Length, 125, 130));

    [Theory]
    [InlineData(ProjectBriefingPresentationTheme.EditorialLight)]
    [InlineData(ProjectBriefingPresentationTheme.GraphiteDark)]
    public void FullPoints_ShowPhotoAndEveryPointUnderItsHeadingWithoutBriefOrCapability(ProjectBriefingPresentationTheme theme)
    {
        var composition = Compose(Deck(Project(), theme: theme));

        Assert.Equal(ProjectBriefingSlideRole.ProjectKeyPointsOnly, Assert.Single(composition.Outline).Role);
        Assert.Equal(901, composition.Outline[0].ProjectId);
        ProjectBriefingPresentationIntegrityValidator.Validate(composition.Content, 1);

        WithSlide(composition, slide =>
        {
            var text = SlideText(slide);
            foreach (var definition in ProjectKeyPointCatalog.All)
            {
                Assert.True(HasShape(slide, $"{definition.Heading} card"), definition.Heading);
            }

            foreach (var point in RealisticPoints)
            {
                Assert.Contains(point, text, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("CAPABILITY OVERVIEW", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PROJECT BRIEF", text, StringComparison.Ordinal);
            Assert.DoesNotContain("A procedural trainer", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Replica launcher console", text, StringComparison.Ordinal);
            Assert.False(HasShape(slide, "Key points placeholder"));

            // A large photograph on the left, 16:9 inside its margin; the points to its right.
            var frame = ShapeByName(slide, "Project photograph frame");
            Assert.True(ShapeWidth(frame) / Emu >= 6.0, "The photograph is large.");
            var innerRatio = ((ShapeWidth(frame) / Emu) - .20) / ((ShapeHeight(frame) / Emu) - .20);
            Assert.InRange(innerRatio, (16d / 9d) - .02, (16d / 9d) + .02);
            var firstCard = ShapeByName(slide, "Training gap card");
            Assert.True(ShapeX(firstCard) > ShapeX(frame) + ShapeWidth(frame));
        });
    }

    [Fact]
    public void Points_RunTopToBottomInCatalogOrderAndStayOnTheSlide()
    {
        var composition = Compose(Deck(Project(points: LongestPoints)));

        WithSlide(composition, slide =>
        {
            var cards = ProjectKeyPointCatalog.All
                .Select(definition => ShapeByName(slide, $"{definition.Heading} card"))
                .ToArray();
            for (var index = 1; index < cards.Length; index++)
            {
                Assert.True(ShapeY(cards[index]) >= ShapeY(cards[index - 1]) + ShapeHeight(cards[index - 1]));
            }

            var last = cards[^1];
            Assert.True((ShapeY(last) + ShapeHeight(last)) / Emu <= 6.70 + .001, "The points end above the footer.");
            Assert.True((ShapeY(cards[0]) / Emu) >= 1.28 - .001, "The points start below the title.");
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LongestPoints_FitAtRoomReadableSize(bool showStatus)
    {
        // A three-line status remark leaves the points the least room.
        var status = string.Join(' ', Enumerable.Repeat("Field trials with two units completed; trial report under preparation for the user directorate.", 3));
        var composition = Compose(Deck(Project(points: LongestPoints, status: status), showStatus: showStatus));
        ProjectBriefingPresentationIntegrityValidator.Validate(composition.Content, 1);

        WithSlide(composition, slide =>
        {
            foreach (var definition in ProjectKeyPointCatalog.All)
            {
                var box = ShapeByName(slide, definition.Heading);
                var sizes = box.Descendants<A.RunProperties>()
                    .Select(run => run.FontSize?.Value ?? 0)
                    .ToArray();
                Assert.All(sizes, size => Assert.True(size >= 1050, $"{definition.Heading}: {size / 100d} pt"));
                if (!showStatus)
                {
                    // With the whole column, the longest points stay at 12 pt.
                    Assert.True(sizes.Max() >= 1200, $"{definition.Heading}: {sizes.Max() / 100d} pt");
                }

                // The card holds the heading and every line of text the size needs.
                var textSize = sizes.Max() / 100d;
                var textWidth = ShapeWidth(box) / Emu;
                var charactersPerLine = (int)Math.Floor(textWidth * 72d / (textSize * ProjectBriefingSlideComposer.KeyPointsOnlyCharacterWidthEm));
                var point = LongestPoints[Array.FindIndex(ProjectKeyPointCatalog.All.ToArray(), item => item.Heading == definition.Heading)];
                var lines = (int)Math.Ceiling(point.Length / (double)charactersPerLine);
                var needed = .22 + (lines * textSize * 1.2 / 72d);
                Assert.True(ShapeHeight(box) / Emu >= needed - .001, $"{definition.Heading} needs {needed:0.00} in.");
            }
        });
    }

    [Fact]
    public void ShortPoints_UseTheLargestSize()
    {
        var composition = Compose(Deck(Project()));

        WithSlide(composition, slide =>
        {
            var size = ShapeByName(slide, "Ease of learning").Descendants<A.RunProperties>()
                .Max(run => run.FontSize?.Value ?? 0);
            Assert.Equal(1300, size);
        });
    }

    [Fact]
    public void MissingPoints_ShowThePlaceholderAndNoFallbackText()
    {
        var composition = Compose(Deck(Project(points: Array.Empty<string>())));
        ProjectBriefingPresentationIntegrityValidator.Validate(composition.Content, 1);

        WithSlide(composition, slide =>
        {
            var text = SlideText(slide);
            Assert.Contains("KEY POINTS NOT RECORDED", text, StringComparison.Ordinal);
            Assert.True(HasShape(slide, "Key points placeholder"));
            Assert.DoesNotContain("Replica launcher console", text, StringComparison.Ordinal);
            Assert.DoesNotContain("A procedural trainer", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Training gap", text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void BlankPoints_CountAsMissing()
    {
        var project = Project();
        project = WithPoints(project, new[] { new ProjectBriefingKeyPoint("Training gap", "   ") });
        var composition = Compose(Deck(project));

        WithSlide(composition, slide => Assert.True(HasShape(slide, "Key points placeholder")));
    }

    [Fact]
    public void SomePoints_ShowOnlyThoseRecorded()
    {
        var project = WithPoints(Project(), new[]
        {
            new ProjectBriefingKeyPoint("Training gap", RealisticPoints[0]),
            new ProjectBriefingKeyPoint("Ease of learning", RealisticPoints[3])
        });
        var composition = Compose(Deck(project));

        WithSlide(composition, slide =>
        {
            Assert.True(HasShape(slide, "Training gap card"));
            Assert.True(HasShape(slide, "Ease of learning card"));
            Assert.False(HasShape(slide, "Op necessity card"));
            Assert.False(HasShape(slide, "Key points placeholder"));
            // Two points are not stretched over the whole column.
            Assert.True(ShapeHeight(ShapeByName(slide, "Training gap card")) / Emu <= 1.30 + .001);
        });
    }

    // Photos are never cropped (owner, 17 Nov 2026): a portrait photo keeps its proportions, whole,
    // inside its frame.
    [Fact]
    public void APortraitPhoto_IsPlacedWhole_NotCroppedOrStretched()
    {
        var project = Project();
        project = WithPhoto(project, TestImageFactory.SolidJpeg(900, 1600, new SKColor(0x6B, 0x7F, 0x5A)));
        var composition = Compose(Deck(project));

        WithSlide(composition, slide =>
        {
            var picture = Assert.Single(slide.Slide.Descendants<P.Picture>().Where(item =>
                item.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name?.Value?.EndsWith("cover photograph", StringComparison.Ordinal) == true));
            var extents = picture.ShapeProperties!.Transform2D!.Extents!;
            Assert.Equal(900d / 1600d, extents.Cx!.Value / (double)extents.Cy!.Value, 2);
        });
    }

    [Fact]
    public void OnAFactSheet_APortraitPhotoIsPlacedWhole_NotCroppedToTheFrame()
    {
        var project = WithPhoto(Project(), TestImageFactory.SolidJpeg(900, 1600, new SKColor(0x6B, 0x7F, 0x5A)));
        var deck = Deck(project);
        var data = new ProjectBriefingPresentationData
        {
            DeckId = deck.DeckId,
            DeckName = deck.DeckName,
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            PresentationMode = deck.PresentationMode,
            PresentationTheme = deck.PresentationTheme,
            CostMode = deck.CostMode,
            StandardSlideOptions = deck.StandardSlideOptions,
            GeneratedAtUtc = deck.GeneratedAtUtc,
            Projects = deck.Projects,
            Summary = deck.Summary
        };
        var composition = new ProjectBriefingSlideComposer(new TestEnvironment(
                Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot")))
            .ComposeWithOutline(data);

        using var stream = new MemoryStream(composition.Content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var photos = document.PresentationPart!.SlideParts
            .SelectMany(slide => slide.Slide.Descendants<P.Picture>())
            .Where(item => item.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name?.Value == $"{project.ProjectName} photograph")
            .ToList();
        var extents = Assert.Single(photos).ShapeProperties!.Transform2D!.Extents!;
        Assert.Equal(900d / 1600d, extents.Cx!.Value / (double)extents.Cy!.Value, 2);
    }

    [Fact]
    public void NoPhoto_ShowsThePhotographPlaceholder()
    {
        var composition = Compose(Deck(Project(photo: false)));
        ProjectBriefingPresentationIntegrityValidator.Validate(composition.Content, 1);

        WithSlide(composition, slide =>
        {
            Assert.Contains("PHOTOGRAPH NOT AVAILABLE", SlideText(slide), StringComparison.Ordinal);
            Assert.True(HasShape(slide, "Project photograph placeholder"));
            Assert.DoesNotContain(
                slide.Slide.Descendants<P.Picture>(),
                picture => picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name?.Value?.EndsWith("cover photograph", StringComparison.Ordinal) == true);
        });
    }

    [Fact]
    public void Status_WhenShown_SitsUnderThePhotoAndPoints()
    {
        var composition = Compose(Deck(Project(), showStatus: true));

        WithSlide(composition, slide =>
        {
            var text = SlideText(slide);
            Assert.Contains("PRESENT STATUS", text, StringComparison.Ordinal);
            Assert.Contains("Field trials with two units under way", text, StringComparison.Ordinal);
            var band = ShapeByName(slide, "Status and key points band");
            var lastCard = ShapeByName(slide, "Proliferation potential card");
            var frame = ShapeByName(slide, "Project photograph frame");
            Assert.True(ShapeY(band) >= ShapeY(lastCard) + ShapeHeight(lastCard));
            Assert.True(ShapeY(band) >= ShapeY(frame) + ShapeHeight(frame));
            Assert.InRange(ShapeWidth(band) / Emu, 12.0, 12.2);
        });
    }

    [Fact]
    public void Status_WhenHiddenOrNotRecorded_LeavesNoBand()
    {
        foreach (var data in new[]
                 {
                     Deck(Project(), showStatus: false),
                     Deck(Project(status: "No external status recorded"), showStatus: true)
                 })
        {
            WithSlide(Compose(data), slide =>
            {
                Assert.DoesNotContain("PRESENT STATUS", SlideText(slide), StringComparison.Ordinal);
                Assert.False(HasShape(slide, "Status and key points band"));
                var lastCard = ShapeByName(slide, "Proliferation potential card");
                Assert.InRange((ShapeY(lastCard) + ShapeHeight(lastCard)) / Emu, 6.60, 6.701);
            });
        }
    }

    [Theory]
    [InlineData(ProjectBriefingCostMode.Both, true, true)]
    [InlineData(ProjectBriefingCostMode.CostRdOnly, true, false)]
    [InlineData(ProjectBriefingCostMode.ProliferationOnly, false, true)]
    [InlineData(ProjectBriefingCostMode.None, false, false)]
    public void CostModes_ShapeTheLineUnderTheTitle(ProjectBriefingCostMode costMode, bool costRd, bool proliferation)
    {
        var composition = Compose(Deck(Project(), costMode: costMode));

        WithSlide(composition, slide =>
        {
            var meta = string.Concat(ShapeByName(slide, "Stage and cost").Descendants<A.Text>().Select(node => node.Text));
            Assert.Contains("Development", meta, StringComparison.Ordinal);
            Assert.Equal(costRd, meta.Contains("Cost (R&D) ₹4.20 Cr AoN", StringComparison.Ordinal));
            Assert.Equal(proliferation, meta.Contains("Proliferation ₹1.10 Cr", StringComparison.Ordinal));
            // Cost stays on the modest line: no cost cards.
            Assert.DoesNotContain("PROLIFERATION COST", SlideText(slide), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void NoStageOrCost_KeepsTheLifecycleSubtitle()
    {
        var composition = Compose(Deck(Project(), costMode: ProjectBriefingCostMode.None, showStage: false));

        WithSlide(composition, slide =>
        {
            Assert.False(HasShape(slide, "Stage and cost"));
            Assert.Contains("Ongoing · Simulator", SlideText(slide), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void RoundedShapes_KeepTheCappedCorner()
    {
        var composition = Compose(Deck(Project(), showStatus: true));

        WithSlide(composition, slide =>
        {
            foreach (var shape in slide.Slide.Descendants<P.Shape>())
            {
                var geometry = shape.ShapeProperties?.GetFirstChild<A.PresetGeometry>();
                if (geometry?.Preset?.Value != A.ShapeTypeValues.RoundRectangle)
                {
                    continue;
                }

                var adjust = geometry.Descendants<A.ShapeGuide>().FirstOrDefault()?.Formula?.Value;
                Assert.NotNull(adjust);
                var value = double.Parse(adjust!.Replace("val ", string.Empty, StringComparison.Ordinal));
                var shortSide = Math.Min(ShapeWidth(shape), ShapeHeight(shape)) / Emu;
                Assert.True(value / 100000d * shortSide <= .12 + .001, shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value);
            }
        });
    }

    [Fact]
    public void TableRowOnlyProjects_GetNoSlide()
    {
        var listed = Project();
        var rowOnly = new ProjectBriefingPresentationProject
        {
            ProjectId = 902,
            ProjectName = "TABLE ROW",
            LifecycleDisplay = "Ongoing",
            TableRowOnly = true,
            SortOrder = 2
        };
        var data = Deck(listed);
        data = new ProjectBriefingPresentationData
        {
            DeckId = data.DeckId,
            DeckName = data.DeckName,
            CostMode = data.CostMode,
            StandardSlideOptions = data.StandardSlideOptions,
            GeneratedAtUtc = data.GeneratedAtUtc,
            Projects = new[] { listed, rowOnly },
            Summary = data.Summary
        };

        var composition = Compose(data);
        Assert.Equal(901, Assert.Single(composition.Outline).ProjectId);
    }

    [Fact]
    public void PreviewRenderer_DrawsThePointsAndPhoto()
    {
        var composition = Compose(Deck(Project(), showStatus: true));
        var preview = ProjectBriefingSlidePreviewRenderer.Render(composition.Content, DateTimeOffset.UtcNow);

        var slide = Assert.Single(preview.Slides);
        Assert.Contains("Proliferation potential", slide.Html, StringComparison.Ordinal);
        Assert.Contains("Every bridge panel, transom and pin modelled to drawing.", slide.Html, StringComparison.Ordinal);
        Assert.Contains("data-pbd-preview-image", slide.Html, StringComparison.Ordinal);
        Assert.Contains(preview.Images.Values, image => image.StartsWith("data:image/jpeg", StringComparison.Ordinal));
    }

    private static ProjectBriefingPresentationProject Project(
        IReadOnlyList<string>? points = null,
        bool photo = true,
        string status = "Field trials with two units under way; trial report due Dec 2026.")
    {
        var texts = points ?? RealisticPoints;
        return new ProjectBriefingPresentationProject
        {
            ProjectId = 901,
            ProjectName = "VR BAILEY BRIDGE SML",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = StageCodes.DEVP,
            PresentStage = "Development",
            PresentStageOrder = ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, StageCodes.DEVP),
            ProjectCategory = "Simulator",
            ExternalStatus = status,
            BriefDescription = "- Replica launcher console\n- Day and night scenarios",
            ProjectBrief = "A procedural trainer for bridge-laying crews.",
            CoverPhoto = photo ? TestImageFactory.SolidJpeg(1600, 900, new SKColor(0x6B, 0x7F, 0x5A)) : null,
            CoverPhotoContentType = photo ? "image/jpeg" : null,
            CostRd = new ProjectBriefingCostValue(42_000_000m, ProjectBriefingCostBasis.AoN, "₹4.20 Cr", "AoN"),
            ProliferationCost = new ProjectBriefingCostValue(11_000_000m, ProjectBriefingCostBasis.Proliferation, "₹1.10 Cr", "Proliferation"),
            KeyPoints = ProjectKeyPointCatalog.All
                .Zip(texts, (definition, text) => new ProjectBriefingKeyPoint(definition.Heading, text))
                .ToArray(),
            SortOrder = 1
        };
    }

    private static ProjectBriefingPresentationProject WithPoints(
        ProjectBriefingPresentationProject project,
        IReadOnlyList<ProjectBriefingKeyPoint> points)
        => new()
        {
            ProjectId = project.ProjectId,
            ProjectName = project.ProjectName,
            LifecycleStatus = project.LifecycleStatus,
            LifecycleDisplay = project.LifecycleDisplay,
            PresentStageCode = project.PresentStageCode,
            PresentStage = project.PresentStage,
            PresentStageOrder = project.PresentStageOrder,
            ProjectCategory = project.ProjectCategory,
            ExternalStatus = project.ExternalStatus,
            BriefDescription = project.BriefDescription,
            ProjectBrief = project.ProjectBrief,
            CoverPhoto = project.CoverPhoto,
            CoverPhotoContentType = project.CoverPhotoContentType,
            CostRd = project.CostRd,
            ProliferationCost = project.ProliferationCost,
            KeyPoints = points,
            SortOrder = project.SortOrder
        };

    private static ProjectBriefingPresentationProject WithPhoto(ProjectBriefingPresentationProject project, byte[] photo)
    {
        var copy = WithPoints(project, project.KeyPoints);
        copy.CoverPhoto = photo;
        copy.CoverPhotoContentType = "image/jpeg";
        return copy;
    }

    private static ProjectBriefingPresentationData Deck(
        ProjectBriefingPresentationProject project,
        ProjectBriefingPresentationTheme theme = ProjectBriefingPresentationTheme.EditorialLight,
        bool showStatus = false,
        bool showStage = true,
        ProjectBriefingCostMode costMode = ProjectBriefingCostMode.Both)
        => new()
        {
            DeckId = 901,
            DeckName = "Visitors",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            PresentationTheme = theme,
            CostMode = costMode,
            StandardSlideOptions = new ProjectBriefingStandardSlideOptions(
                ProjectBriefingProjectBriefLayout.Automatic,
                ShowPresentStage: showStage,
                ShowPresentStatus: showStatus,
                IncludeStageDistributionTable: false),
            GeneratedAtUtc = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

    private static ProjectBriefingComposition Compose(ProjectBriefingPresentationData data)
        => new ProjectBriefingSlideComposer(new TestEnvironment(
                Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot")))
            .ComposeKeyPointsOnlySlides(data);

    private static void WithSlide(ProjectBriefingComposition composition, Action<SlidePart> assert)
    {
        using var stream = new MemoryStream(composition.Content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var part = document.PresentationPart!;
        var first = part.Presentation.SlideIdList!.Elements<P.SlideId>().First();
        assert((SlidePart)part.GetPartById(first.RelationshipId!));
    }

    private static string SlideText(SlidePart slide)
        => string.Join(" ", slide.Slide.Descendants<A.Text>().Select(node => node.Text));

    private static bool HasShape(SlidePart slide, string name)
        => slide.Slide.Descendants<P.Shape>().Any(shape => string.Equals(
            shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value,
            name,
            StringComparison.Ordinal));

    private static P.Shape ShapeByName(SlidePart slide, string name)
        => Assert.Single(slide.Slide.Descendants<P.Shape>().Where(shape => string.Equals(
            shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value,
            name,
            StringComparison.Ordinal)));

    private static double ShapeX(P.Shape shape) => shape.ShapeProperties!.Transform2D!.Offset!.X!.Value;
    private static double ShapeY(P.Shape shape) => shape.ShapeProperties!.Transform2D!.Offset!.Y!.Value;
    private static double ShapeWidth(P.Shape shape) => shape.ShapeProperties!.Transform2D!.Extents!.Cx!.Value;
    private static double ShapeHeight(P.Shape shape) => shape.ShapeProperties!.Transform2D!.Extents!.Cy!.Value;

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public TestEnvironment(string root)
        {
            ContentRootPath = root;
            WebRootPath = Path.Combine(root, "wwwroot");
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
