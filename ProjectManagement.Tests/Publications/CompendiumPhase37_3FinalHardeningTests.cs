using ProjectManagement.Models;
using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase37_3FinalHardeningTests
{
    [Fact]
    public void AdditionalNotePolicy_NormalizesWithoutHardTruncationAndProvidesSoftGuidance()
    {
        var longNote = new string('A', CompendiumPublicationNotePolicy.StrongAdvisoryCharacterCount + 50);
        var normalized = CompendiumPublicationNotePolicy.Normalize("  First line\r\n\r\nSecond line  ");

        Assert.Equal("First line\n\nSecond line", normalized);
        Assert.Equal(longNote.Length, CompendiumPublicationNotePolicy.Normalize(longNote).Length);
        Assert.NotNull(CompendiumPublicationNotePolicy.EditorialAdvisory(longNote));
        Assert.Null(CompendiumPublicationNotePolicy.EditorialAdvisory("Short publication note."));
    }

    [Fact]
    public void ReviewFingerprint_ChangesWhenAdditionalNoteChanges()
    {
        var baseline = CompendiumReviewFingerprint.Create(CreateFingerprintInput("Note A", mediaVersion: 3));
        var changed = CompendiumReviewFingerprint.Create(CreateFingerprintInput("Note B", mediaVersion: 3));

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void ReviewFingerprint_ChangesWhenSamePhotoIsReprocessed()
    {
        var baseline = CompendiumReviewFingerprint.Create(CreateFingerprintInput("Same note", mediaVersion: 3));
        var changed = CompendiumReviewFingerprint.Create(CreateFingerprintInput("Same note", mediaVersion: 4));

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void ShallowFit_RemainsAllowedButReturnsEditorialAdvisory()
    {
        var warning = CompendiumDossierEditorialPolicy.ShallowFitWarning(
            CompendiumImageFitMode.Fit,
            hasPhoto: true,
            renderedImageHeightPoints: CompendiumDossierEditorialPolicy.ShallowFitWarningHeightPoints - 10f);

        Assert.NotNull(warning);
        Assert.Contains("Fill", warning!, StringComparison.OrdinalIgnoreCase);
        Assert.True(CompendiumDossierEditorialPolicy.IsImageGeometryEditoriallyValid(
            CompendiumDossierLayout.Balanced,
            CompendiumImageFitMode.Fit,
            hasPhoto: true,
            renderedImageHeightPoints: 30f));
    }

    [Fact]
    public void PhysicalContinuation_PreservesParagraphAndSentenceContentWithoutWordSlicing()
    {
        var source = string.Join("\n\n", Enumerable.Range(1, 8).Select(index =>
            $"Paragraph {index}. This is a complete sentence used to exercise physical continuation planning. " +
            "Another complete sentence remains intact when the page boundary is reached."));

        var pages = CompendiumDossierNarrativeFlowPlanner.SplitForPhysicalPages(
            source,
            widthPoints: 519f,
            pageHeightPoints: 100f,
            narrativeFontScale: 1f,
            includeHeading: false);

        Assert.True(pages.Count > 1);
        static string CollapseWhitespace(string value)
            => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        Assert.Equal(
            CollapseWhitespace(source),
            CollapseWhitespace(string.Join("\n\n", pages)));
    }

    [Fact]
    public void TechnicalSpecificationColumns_AreResolvedFromPhysicalLineMeasurement()
    {
        var compact = new[] { "WiFi", "Rugged carrying case", "24-inch display" };
        var longItems = new[]
        {
            string.Join(" ", Enumerable.Repeat("WWWWWWWWWW high bandwidth rugged workstation integration requirement", 5)),
            string.Join(" ", Enumerable.Repeat("Extended weapon interface and environmental qualification requirement", 5))
        };

        Assert.Equal(3, CompendiumDossierPaginationPlanner.ResolveTechnicalSpecificationColumns(compact));
        Assert.Equal(1, CompendiumDossierPaginationPlanner.ResolveTechnicalSpecificationColumns(longItems));
    }

    [Fact]
    public void LongAdditionalNote_ParticipatesInPhysicalPaginationInsteadOfBeingClipped()
    {
        var note = string.Join("\n\n", Enumerable.Range(1, 18).Select(index =>
            $"Additional publication note paragraph {index}. This information remains after the main dossier content and must paginate naturally when required."));

        var decision = CompendiumDossierPaginationPlanner.Resolve(
            CompendiumDossierLayout.Automatic,
            CompendiumDossierLayout.Technical,
            availablePhotoCount: 0,
            narrative: "The Project Brief is present and concise for this pagination test.",
            technicalSpecifications: Array.Empty<string>(),
            programmeModuleCount: 1,
            projectName: "Additional note pagination test",
            additionalNote: note);

        Assert.True(decision.EstimatedPageCount > 1);
        Assert.True(decision.UsesContinuation);
    }

    private static CompendiumReviewFingerprintInput CreateFingerprintInput(string note, int mediaVersion)
        => new(
            ProjectId: 42,
            ProjectName: "Fingerprint test project",
            LifecycleStatus: ProjectLifecycleStatus.Active,
            ProjectCategory: "R&D",
            TechnicalCategory: "Simulation",
            SponsoringLineDirectorate: "Inf",
            CompletionYear: null,
            ProliferationAvailability: null,
            ProliferationCostLakhs: null,
            Description: "Guaranteed project brief content.",
            ResolvedPhotoId: 100,
            ImageSelectionMode: CompendiumImageSelectionMode.Automatic,
            FocalX: .5d,
            FocalY: .5d)
        {
            NarrativeSource = CompendiumNarrativeSource.ProjectBrief,
            AdditionalNote = note,
            DossierImages = new[]
            {
                new CompendiumDossierImageSelection(
                    CompendiumDossierImageRole.Primary,
                    100,
                    .5d,
                    .5d,
                    CompendiumImageFitMode.Fill,
                    CompendiumPhotoSelectionSource.ProjectCover)
                {
                    PhotoVersion = mediaVersion,
                    SourceWidth = 2400,
                    SourceHeight = 1600
                }
            }
        };
}
