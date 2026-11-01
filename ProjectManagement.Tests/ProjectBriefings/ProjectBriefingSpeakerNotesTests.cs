using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;

namespace ProjectManagement.Tests.ProjectBriefings;

// A project's speaker notes are written into the PowerPoint's notes for its first slide.
public sealed class ProjectBriefingSpeakerNotesTests
{
    [Fact]
    public void Notes_GoOnTheProjectsFirstSlideOnly()
    {
        var notes = "Opening line for the Commandant.\n\nAsk for the TEC <date> & approval.";
        var data = Data(ProjectBriefingLayout.StandardBriefing, notes, longCapability: true);

        var composition = Composer().ComposeWithOutline(data);

        using var document = PresentationDocument.Open(new MemoryStream(composition.Content, writable: false), false);
        var slides = OrderedSlides(document);
        var withNotes = slides
            .Select((slide, index) => (slide, index))
            .Where(pair => pair.slide.NotesSlidePart is not null)
            .ToArray();
        var (notedSlide, notedIndex) = Assert.Single(withNotes);

        // The first outline entry for project 1 is the slide that carries its notes.
        var firstProjectSlide = composition.Outline.First(item => item.ProjectId == 1);
        Assert.Equal(firstProjectSlide.Number, notedIndex + 1);
        Assert.Contains(composition.Outline, item => item.ProjectId == 1 && item.Role == ProjectBriefingSlideRole.ProjectCapabilityContinuation);

        var paragraphs = notedSlide.NotesSlidePart!.NotesSlide.Descendants<A.Paragraph>()
            .Select(paragraph => string.Concat(paragraph.Descendants<A.Text>().Select(text => text.Text)))
            .ToArray();
        Assert.Equal(new[] { "Opening line for the Commandant.", "", "Ask for the TEC <date> & approval." }, paragraphs);
        Assert.NotNull(notedSlide.NotesSlidePart.NotesMasterPart);
        Assert.NotNull(document.PresentationPart!.NotesMasterPart);
        Assert.Single(document.PresentationPart.Presentation.NotesMasterIdList!.Elements<NotesMasterId>());
    }

    [Fact]
    public void UpdateSheets_CarryNotesToo()
    {
        var composition = Composer().ComposeWithOutline(Data(ProjectBriefingLayout.ProjectUpdateSheet, "Sheet notes."));

        using var document = PresentationDocument.Open(new MemoryStream(composition.Content, writable: false), false);
        var noted = Assert.Single(OrderedSlides(document), slide => slide.NotesSlidePart is not null);
        Assert.Contains("Sheet notes.", noted.NotesSlidePart!.NotesSlide.InnerText);
    }

    [Fact]
    public void ADeckWithoutNotes_HasNoNotesParts()
    {
        var composition = Composer().ComposeWithOutline(Data(ProjectBriefingLayout.StandardBriefing, notes: null));

        using var document = PresentationDocument.Open(new MemoryStream(composition.Content, writable: false), false);
        Assert.Null(document.PresentationPart!.NotesMasterPart);
        Assert.All(OrderedSlides(document), slide => Assert.Null(slide.NotesSlidePart));
    }

    [Fact]
    public void TheSlidePreview_IsUnchangedByNotes()
    {
        var withNotes = Composer().ComposeWithOutline(Data(ProjectBriefingLayout.StandardBriefing, "Notes."));
        var withoutNotes = Composer().ComposeWithOutline(Data(ProjectBriefingLayout.StandardBriefing, notes: null));

        var generatedAt = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal(
            ProjectBriefingSlidePreviewRenderer.Render(withoutNotes.Content, generatedAt).Slides.Select(slide => slide.Html),
            ProjectBriefingSlidePreviewRenderer.Render(withNotes.Content, generatedAt).Slides.Select(slide => slide.Html));
    }

    private static SlidePart[] OrderedSlides(PresentationDocument document)
    {
        var part = document.PresentationPart!;
        return part.Presentation.SlideIdList!.Elements<SlideId>()
            .Select(id => (SlidePart)part.GetPartById(id.RelationshipId!))
            .ToArray();
    }

    private static ProjectBriefingSlideComposer Composer()
        => new(new TestEnvironment(Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot")));

    private static ProjectBriefingPresentationData Data(ProjectBriefingLayout layout, string? notes, bool longCapability = false)
        => new()
        {
            DeckId = 701,
            DeckName = "Speaker notes",
            Layout = layout,
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            CostMode = ProjectBriefingCostMode.CostRdOnly,
            NarrativeMode = ProjectBriefingNarrativeMode.CapabilityOverview,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            GeneratedAtUtc = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero),
            Projects = new[]
            {
                Project(1, "Noted project", 10, notes, longCapability),
                Project(2, "Quiet project", 20, null, false)
            },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 2, OngoingCount = 2 }
        };

    private static ProjectBriefingPresentationProject Project(int id, string name, int sortOrder, string? notes, bool longCapability)
        => new()
        {
            ProjectId = id,
            ProjectName = name,
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = "DEVP",
            PresentStage = "Development",
            PresentStageOrder = ProjectBriefingStageOrder.Development,
            CostRd = ProjectBriefingCostValue.Missing(),
            ExternalStatus = "Status recorded.",
            BriefDescription = longCapability
                ? string.Join("\n", Enumerable.Range(1, 40).Select(line => $"• Capability statement {line} with enough words to need room on the slide."))
                : "Capability description.",
            ProjectBrief = "A short project brief.",
            SortOrder = sortOrder,
            SpeakerNotes = notes
        };

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
