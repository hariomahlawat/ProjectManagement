using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services;
using ProjectManagement.Services.Publications;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Pages.Projects.Publications.Brochure;

[Authorize]
public sealed class IndexModel : PageModel
{
    private const int MaximumSelectedProjects = 100;

    private readonly IBrochurePublicationService _publicationService;
    private readonly IBrochurePhotoService _photoService;
    private readonly IBrochurePdfReportBuilder _pdfBuilder;
    private readonly IPublicationFontService _fontService;
    private readonly IClock _clock;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IBrochurePublicationService publicationService,
        IBrochurePhotoService photoService,
        IBrochurePdfReportBuilder pdfBuilder,
        IPublicationFontService fontService,
        IClock clock,
        ILogger<IndexModel> logger)
    {
        _publicationService = publicationService ?? throw new ArgumentNullException(nameof(publicationService));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
        _pdfBuilder = pdfBuilder ?? throw new ArgumentNullException(nameof(pdfBuilder));
        _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public GenerateBrochureInput Input { get; set; } = new();

    public IReadOnlyList<BrochureProjectListItemVm> Projects { get; private set; }
        = Array.Empty<BrochureProjectListItemVm>();

    public IReadOnlyList<string> ProjectCategories { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> TechnicalCategories { get; private set; } = Array.Empty<string>();
    public PublicationFontStatus FontStatus { get; private set; }
        = new(
            PublicationFontService.FallbackFamilyName,
            PublicationFontService.FallbackFamilyName,
            false,
            false,
            Array.Empty<string>(),
            "QuestPDF bundled fallback");

    public BrochurePrintMatter ApprovedPrintMatter => BrochurePrintPublicationPolicy.ApprovedReference;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ApplyDefaults(includePrintMatter: true);
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnGetPhotoAsync(
        int projectId,
        int photoId,
        string? mode,
        int v,
        CancellationToken cancellationToken)
    {
        var kind = string.Equals(mode, "source", StringComparison.OrdinalIgnoreCase)
            ? BrochurePhotoPreviewKind.Source
            : BrochurePhotoPreviewKind.Thumbnail;
        var preview = await _photoService.GetPreviewAsync(
            projectId,
            photoId,
            kind,
            cancellationToken);
        if (preview is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private,max-age=86400";
        Response.Headers["X-PRISM-Publication-Photo-Source"] = preview.SourceVariant;
        Response.Headers["X-PRISM-Publication-Photo-Size"] = $"{preview.SourceWidth}x{preview.SourceHeight}";
        Response.Headers["X-PRISM-Publication-Photo-Quality"] = preview.Quality.ToString();
        return File(preview.Content, preview.ContentType);
    }


    public async Task<IActionResult> OnPostProjectStateAsync(CancellationToken cancellationToken)
    {
        ApplyDefaults();
        NormalizeInput();

        if (!Enum.IsDefined(Input.NarrativeSource))
        {
            return BadRequest(new { message = "Select a valid project narrative source." });
        }

        var reviewProjects = await _publicationService.GetReviewProjectsAsync(
            Input.Selections.Select(selection => selection.ProjectId).ToArray(),
            Input.NarrativeSource,
            cancellationToken);

        var photoReferences = reviewProjects
            .SelectMany(project => project.Photos.Select(photo =>
                new BrochurePhotoReference(project.ProjectId, photo.PhotoId)))
            .ToArray();
        var probes = await _photoService.ProbeAsync(photoReferences, cancellationToken);

        return new JsonResult(new
        {
            projects = reviewProjects.Select(ToClientReviewProject).ToArray(),
            photoProbes = probes.ToDictionary(
                pair => pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                pair => new
                {
                    pair.Value.IsReady,
                    pair.Value.Width,
                    pair.Value.Height,
                    quality = pair.Value.Quality.ToString(),
                    pair.Value.SourceVariant
                })
        });
    }

    public async Task<IActionResult> OnPostPreflightAsync(CancellationToken cancellationToken)
    {
        ApplyDefaults();
        NormalizeInput();

        if (!Enum.IsDefined(Input.NarrativeSource)
            || !Enum.IsDefined(Input.CoverStyle)
            || !Enum.IsDefined(Input.PublicationProfile)
            || !Enum.IsDefined(Input.InstitutionalCoverArtwork))
        {
            var message = !Enum.IsDefined(Input.NarrativeSource)
                ? "Select a valid project narrative source."
                : !Enum.IsDefined(Input.CoverStyle)
                    ? "Select a valid brochure cover style."
                    : !Enum.IsDefined(Input.PublicationProfile)
                        ? "Select a valid brochure publication profile."
                        : "Select a valid institutional cover artwork.";
            var code = !Enum.IsDefined(Input.NarrativeSource)
                ? "invalidNarrativeSource"
                : !Enum.IsDefined(Input.CoverStyle)
                    ? "invalidCoverStyle"
                    : !Enum.IsDefined(Input.PublicationProfile)
                        ? "invalidPublicationProfile"
                        : "invalidInstitutionalCoverArtwork";

            return new JsonResult(new
            {
                selectedProjectCount = Input.Selections.Count,
                blockerCount = 1,
                warningCount = 0,
                informationCount = 0,
                canGenerate = false,
                issues = new[]
                {
                    new
                    {
                        severity = "blocker",
                        code,
                        projectId = (int?)null,
                        projectName = (string?)null,
                        message
                    }
                }
            });
        }

        var preflight = await _publicationService.PreflightAsync(
            ToSelections(),
            Input.NarrativeSource,
            Input.CoverStyle,
            Input.PublicationProfile,
            Input.AllowTextOnlyProjects,
            Input.CoverHeroProjectId,
            Input.CoverHeroPhotoId,
            ToPrintMatter(),
            Input.Strapline,
            Input.HandlingMarking,
            cancellationToken);
        return new JsonResult(ToClientPreflight(preflight));
    }

    public Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
        => GenerateInternalAsync(preview: true, cancellationToken);

    public Task<IActionResult> OnPostGenerateAsync(CancellationToken cancellationToken)
        => GenerateInternalAsync(preview: false, cancellationToken);

    private async Task<IActionResult> GenerateInternalAsync(
        bool preview,
        CancellationToken cancellationToken)
    {
        ApplyDefaults();
        NormalizeInput();
        ValidateGenerationInput(preview);

        if (!ModelState.IsValid)
        {
            if (WantsJson())
            {
                return BadRequest(new
                {
                    message = preview
                        ? "The brochure preview could not be prepared."
                        : "The brochure is not ready for final download.",
                    errors = ModelState.Values
                        .SelectMany(value => value.Errors)
                        .Select(error => error.ErrorMessage)
                        .Where(message => !string.IsNullOrWhiteSpace(message))
                        .ToArray()
                });
            }

            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            var generatedAt = _clock.UtcNow;
            var options = new BrochureBuildOptions(
                Input.Title!,
                Input.Subtitle!,
                Input.Edition!,
                Input.Strapline!,
                Input.CoverStyle,
                Input.NarrativeSource,
                Input.PublicationProfile,
                NullIfWhiteSpace(Input.IntroductionTitle),
                NullIfWhiteSpace(Input.IntroductionText),
                NullIfWhiteSpace(Input.HandlingMarking),
                "Simulator Development Division",
                Input.AllowTextOnlyProjects,
                generatedAt,
                Input.CoverHeroProjectId,
                Input.CoverHeroPhotoId,
                Input.CoverHeroFocalX,
                Input.CoverHeroFocalY,
                Input.IncludeBackCover,
                NullIfWhiteSpace(Input.PrintIntroText),
                NullIfWhiteSpace(Input.PrintFutureText),
                NullIfWhiteSpace(Input.PrintProcurementText),
                NullIfWhiteSpace(Input.PrintCentreStatement),
                NullIfWhiteSpace(Input.PrintDevelopingAgencyText),
                NullIfWhiteSpace(Input.PrintManufacturingAgencyText),
                NullIfWhiteSpace(Input.PrintVisionaryText),
                NullIfWhiteSpace(Input.PrintNewSimulatorsText),
                Input.InstitutionalCoverArtwork);

            var publication = await _publicationService.BuildAsync(
                ToSelections(),
                options,
                cancellationToken);
            var bytes = _pdfBuilder.Build(publication);
            var fileName = $"{SanitizeFileName(Input.Title, "SDD_Capability_Brochure")}_{generatedAt:yyyyMMdd}.pdf";

            Response.Headers["X-PRISM-Publication-FileName"] = fileName;
            if (preview)
            {
                Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
                return File(bytes, "application/pdf");
            }

            return File(bytes, "application/pdf", fileName);
        }
        catch (BrochurePublicationValidationException exception)
        {
            var blockerMessages = exception.Preflight.Issues
                .Where(issue => issue.Severity == PublicationIssueSeverity.Blocker)
                .Select(issue => issue.Message)
                .Distinct(StringComparer.Ordinal)
                .Take(12)
                .ToArray();

            if (WantsJson())
            {
                return new JsonResult(new
                {
                    message = "Publication preflight changed while the brochure was being prepared.",
                    errors = blockerMessages
                })
                {
                    StatusCode = StatusCodes.Status409Conflict
                };
            }

            foreach (var message in blockerMessages)
            {
                ModelState.AddModelError(string.Empty, message);
            }

            await LoadAsync(cancellationToken);
            return Page();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Capability brochure {Operation} failed. SelectedProjects={SelectedProjectCount}, Narrative={NarrativeSource}, Cover={CoverStyle}",
                preview ? "preview" : "generation",
                Input.Selections.Count,
                Input.NarrativeSource,
                Input.CoverStyle);
            var message = preview
                ? "The brochure preview could not be generated. Review publication preflight and try again."
                : "The brochure could not be generated. Review publication preflight and try again.";
            if (WantsJson())
            {
                return new JsonResult(new { message })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }

            ModelState.AddModelError(string.Empty, message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Projects = await _publicationService.GetProjectOptionsAsync(cancellationToken);
        ProjectCategories = Projects
            .Select(project => project.ProjectCategory)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        TechnicalCategories = Projects
            .Select(project => project.TechnicalCategory)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        FontStatus = _fontService.CurrentStatus;
    }

    private void ApplyDefaults(bool includePrintMatter = false)
    {
        var year = _clock.UtcNow.Year;
        Input.Title = string.IsNullOrWhiteSpace(Input.Title) ? "SDD Capability Brochure" : Input.Title;
        Input.Subtitle = string.IsNullOrWhiteSpace(Input.Subtitle) ? "Simulator Development Division" : Input.Subtitle;
        Input.Edition = string.IsNullOrWhiteSpace(Input.Edition) ? $"Capability Edition · {year}" : Input.Edition;
        Input.Strapline = string.IsNullOrWhiteSpace(Input.Strapline)
            ? "Simulators of the Army, by the Army, for the Army"
            : Input.Strapline;
        if (includePrintMatter)
        {
            var approved = BrochurePrintPublicationPolicy.ApprovedReference;
            Input.PrintIntroText = string.IsNullOrWhiteSpace(Input.PrintIntroText) ? approved.OpeningNarrative : Input.PrintIntroText;
            Input.PrintFutureText = string.IsNullOrWhiteSpace(Input.PrintFutureText) ? approved.FutureNarrative : Input.PrintFutureText;
            Input.PrintProcurementText = string.IsNullOrWhiteSpace(Input.PrintProcurementText) ? approved.ProcurementGuidance : Input.PrintProcurementText;
            Input.PrintCentreStatement = string.IsNullOrWhiteSpace(Input.PrintCentreStatement) ? approved.CentreStatement : Input.PrintCentreStatement;
            Input.PrintDevelopingAgencyText = string.IsNullOrWhiteSpace(Input.PrintDevelopingAgencyText) ? approved.DevelopingAgency : Input.PrintDevelopingAgencyText;
            Input.PrintManufacturingAgencyText = string.IsNullOrWhiteSpace(Input.PrintManufacturingAgencyText) ? approved.ManufacturingAgency : Input.PrintManufacturingAgencyText;
            Input.PrintVisionaryText = string.IsNullOrWhiteSpace(Input.PrintVisionaryText) ? approved.VisionaryHorizons : Input.PrintVisionaryText;
            Input.PrintNewSimulatorsText = string.IsNullOrWhiteSpace(Input.PrintNewSimulatorsText) ? approved.NewSimulatorsGuidance : Input.PrintNewSimulatorsText;
        }
    }

    private void NormalizeInput()
    {
        Input.Title = Normalize(Input.Title, 120);
        Input.Subtitle = Normalize(Input.Subtitle, 160);
        Input.Edition = Normalize(Input.Edition, 80);
        Input.Strapline = Normalize(Input.Strapline, 180);
        Input.IntroductionTitle = NormalizeOptional(Input.IntroductionTitle, 120);
        Input.IntroductionText = NormalizeOptional(Input.IntroductionText, 3000, preserveLineBreaks: true);
        Input.PrintIntroText = NormalizeOptional(Input.PrintIntroText, 5000, preserveLineBreaks: true);
        Input.PrintFutureText = NormalizeOptional(Input.PrintFutureText, 3500, preserveLineBreaks: true);
        Input.PrintProcurementText = NormalizeOptional(Input.PrintProcurementText, 3500, preserveLineBreaks: true);
        Input.PrintCentreStatement = NormalizeOptional(Input.PrintCentreStatement, 1200, preserveLineBreaks: true);
        Input.PrintDevelopingAgencyText = NormalizeOptional(Input.PrintDevelopingAgencyText, 1800, preserveLineBreaks: true);
        Input.PrintManufacturingAgencyText = NormalizeOptional(Input.PrintManufacturingAgencyText, 1200, preserveLineBreaks: true);
        Input.PrintVisionaryText = NormalizeOptional(Input.PrintVisionaryText, 4500, preserveLineBreaks: true);
        Input.PrintNewSimulatorsText = NormalizeOptional(Input.PrintNewSimulatorsText, 1800, preserveLineBreaks: true);
        Input.HandlingMarking = NormalizeOptional(Input.HandlingMarking, 80)?.ToUpperInvariant();
        Input.CoverHeroProjectId = Input.CoverHeroProjectId is > 0
            ? Input.CoverHeroProjectId
            : null;
        Input.CoverHeroPhotoId = Input.CoverHeroPhotoId is > 0
            ? Input.CoverHeroPhotoId
            : null;
        Input.CoverHeroFocalX = ClampFocal(Input.CoverHeroFocalX);
        Input.CoverHeroFocalY = ClampFocal(Input.CoverHeroFocalY);
        if (Input.CoverHeroProjectId is null && Input.CoverHeroPhotoId is null)
        {
            Input.CoverHeroFocalX = .5d;
            Input.CoverHeroFocalY = .5d;
            Input.CoverReviewed = false;
        }

        Input.Selections = Input.Selections
            .Where(selection => selection.ProjectId > 0)
            .GroupBy(selection => selection.ProjectId)
            .Select(group => group.First())
            .Take(MaximumSelectedProjects + 1)
            .Select(selection =>
            {
                selection.PrimaryPhotoId = NormalizePhotoId(selection.PrimaryPhotoId);
                selection.SecondaryPhotoId = NormalizePhotoId(selection.SecondaryPhotoId);
                selection.PrimaryFocalX = ClampFocal(selection.PrimaryFocalX);
                selection.PrimaryFocalY = ClampFocal(selection.PrimaryFocalY);
                selection.SecondaryFocalX = ClampFocal(selection.SecondaryFocalX);
                selection.SecondaryFocalY = ClampFocal(selection.SecondaryFocalY);
                if (!Enum.IsDefined(selection.ImageMode))
                {
                    selection.ImageMode = BrochureImageMode.Automatic;
                }
                return selection;
            })
            .ToList();
    }

    private void ValidateGenerationInput(bool preview)
    {
        if (Input.Selections.Count == 0)
        {
            ModelState.AddModelError(nameof(Input.Selections), "Select at least one project for the brochure.");
        }
        else if (Input.Selections.Count > MaximumSelectedProjects)
        {
            ModelState.AddModelError(nameof(Input.Selections), $"A brochure can contain up to {MaximumSelectedProjects} selected projects.");
        }

        if (!Enum.IsDefined(Input.CoverStyle))
        {
            ModelState.AddModelError(nameof(Input.CoverStyle), "Select a valid cover style.");
        }
        if (!Enum.IsDefined(Input.InstitutionalCoverArtwork))
        {
            ModelState.AddModelError(nameof(Input.InstitutionalCoverArtwork), "Select a valid institutional cover artwork.");
        }
        if (!Enum.IsDefined(Input.NarrativeSource))
        {
            ModelState.AddModelError(nameof(Input.NarrativeSource), "Select a valid project narrative source.");
        }
        if (!Enum.IsDefined(Input.PublicationProfile))
        {
            ModelState.AddModelError(nameof(Input.PublicationProfile), "Select a valid brochure publication profile.");
        }

        if (Input.PublicationProfile == BrochurePublicationProfile.PrintCompact)
        {
            foreach (var issue in BrochurePrintPublicationPolicy.Validate(
                         Input.PublicationProfile,
                         ToPrintMatter()))
            {
                ModelState.AddModelError(nameof(Input.PublicationProfile), issue.Message);
            }
        }

        if (!preview && Input.Selections.Count > 0)
        {
            var unreviewed = Input.Selections.Count(selection => !selection.IsReviewed);
            if (unreviewed > 0)
            {
                ModelState.AddModelError(
                    nameof(Input.Selections),
                    $"Review all selected projects before final download. {unreviewed} project{(unreviewed == 1 ? string.Empty : "s")} still require review.");
            }

            if (Input.CoverStyle == BrochureCoverStyle.Contemporary && !Input.CoverReviewed)
            {
                ModelState.AddModelError(
                    nameof(Input.CoverReviewed),
                    "Approve the Cover B hero and crop before final download.");
            }
        }
    }

    private BrochurePrintMatter ToPrintMatter()
        => new(
            NullIfWhiteSpace(Input.PrintCentreStatement),
            NullIfWhiteSpace(Input.PrintIntroText),
            NullIfWhiteSpace(Input.PrintFutureText),
            NullIfWhiteSpace(Input.PrintProcurementText),
            NullIfWhiteSpace(Input.PrintDevelopingAgencyText),
            NullIfWhiteSpace(Input.PrintManufacturingAgencyText),
            NullIfWhiteSpace(Input.PrintVisionaryText),
            NullIfWhiteSpace(Input.PrintNewSimulatorsText));

    private IReadOnlyList<BrochureProjectSelection> ToSelections()
        => Input.Selections
            .Select(selection => new BrochureProjectSelection(
                selection.ProjectId,
                selection.PrimaryPhotoId,
                selection.SecondaryPhotoId,
                selection.PrimaryFocalX,
                selection.PrimaryFocalY,
                selection.SecondaryFocalX,
                selection.SecondaryFocalY,
                selection.ImageMode,
                selection.PrimaryPhotoConfirmed,
                selection.IsReviewed))
            .ToArray();

    private static object ToClientPreflight(BrochurePreflight preflight)
        => new
        {
            selectedProjectCount = preflight.SelectedProjectCount,
            blockerCount = preflight.BlockerCount,
            warningCount = preflight.WarningCount,
            informationCount = preflight.InformationCount,
            canGenerate = preflight.CanGenerate,
            isPublicationReady = preflight.IsPublicationReady,
            resolvedCoverHeroProjectId = preflight.ResolvedCoverHeroProjectId,
            resolvedCoverHeroPhotoId = preflight.ResolvedCoverHeroPhotoId,
            resolvedCoverHeroWidth = preflight.ResolvedCoverHeroWidth,
            resolvedCoverHeroHeight = preflight.ResolvedCoverHeroHeight,
            resolvedCoverHeroQuality = preflight.ResolvedCoverHeroQuality?.ToString(),
            estimatedPageCount = preflight.EstimatedPageCount,
            estimatedAveragePageUtilizationPercent = preflight.EstimatedAveragePageUtilizationPercent,
            estimatedClosingPageProjectCount = preflight.EstimatedClosingPageProjectCount,
            closingMatterSharesFinalPage = preflight.ClosingMatterSharesFinalPage,
            lowestProjectPageUtilizationPercent = preflight.LowestProjectPageUtilizationPercent,
            finalPageUtilizationPercent = preflight.FinalPageUtilizationPercent,
            printFrontPageUsesMinimumTypography = preflight.PrintFrontPageUsesMinimumTypography,
            printSheetPlan = preflight.PrintSheetPlan?.Select(sheet => new
            {
                sheet.SheetNumber,
                sheet.Kind,
                sheet.FirstProjectOrdinal,
                sheet.LastProjectOrdinal,
                sheet.ProjectCount,
                sheet.IncludesClosingMatter,
                sheet.UtilizationPercent,
                sheet.Label
            }).ToArray(),
            smartFlowSuggestion = preflight.SmartFlowSuggestion is null ? null : new
            {
                suggestedProjectIds = preflight.SmartFlowSuggestion.SuggestedProjectIds,
                preflight.SmartFlowSuggestion.CurrentPageCount,
                preflight.SmartFlowSuggestion.SuggestedPageCount,
                preflight.SmartFlowSuggestion.CurrentLowestProjectUtilizationPercent,
                preflight.SmartFlowSuggestion.SuggestedLowestProjectUtilizationPercent,
                preflight.SmartFlowSuggestion.CurrentAverageUtilizationPercent,
                preflight.SmartFlowSuggestion.SuggestedAverageUtilizationPercent,
                preflight.SmartFlowSuggestion.MovedProjectCount,
                preflight.SmartFlowSuggestion.TotalPositionShift,
                preflight.SmartFlowSuggestion.DenseProjectCount,
                preflight.SmartFlowSuggestion.AutomaticSingleProjectCount,
                preflight.SmartFlowSuggestion.MinimumImageWidthPoints,
                preflight.SmartFlowSuggestion.AdaptiveTreatmentSummary,
                preflight.SmartFlowSuggestion.Summary,
                moves = preflight.SmartFlowSuggestion.Moves.Select(move => new
                {
                    move.ProjectId,
                    move.ProjectName,
                    move.FromOrdinal,
                    move.ToOrdinal
                }).ToArray(),
                suggestedSheetPlan = preflight.SmartFlowSuggestion.SuggestedSheetPlan.Select(sheet => new
                {
                    sheet.SheetNumber,
                    sheet.Kind,
                    sheet.FirstProjectOrdinal,
                    sheet.LastProjectOrdinal,
                    sheet.ProjectCount,
                    sheet.IncludesClosingMatter,
                    sheet.UtilizationPercent,
                    sheet.Label
                }).ToArray()
            },
            issues = preflight.Issues.Select(issue => new
            {
                severity = issue.Severity.ToString().ToLowerInvariant(),
                code = issue.Code.ToString(),
                issue.ProjectId,
                issue.ProjectName,
                issue.Message
            }).ToArray()
        };


    private object ToClientReviewProject(BrochureProjectReviewVm project)
        => new
        {
            project.ProjectId,
            project.ProjectName,
            project.Lifecycle,
            project.ProjectCategory,
            project.TechnicalCategory,
            project.Narrative,
            project.HasNarrative,
            project.NarrativeWordCount,
            project.HasProjectBrief,
            project.HasCapabilityOverview,
            project.HasFullDescription,
            project.ProjectBriefWordCount,
            project.CapabilityOverviewWordCount,
            project.FullDescriptionWordCount,
            project.DefaultPrimaryPhotoId,
            overviewUrl = Url.Page("/Projects/Overview", new { id = project.ProjectId, content = NarrativeContentTab(Input.NarrativeSource) }),
            photosUrl = Url.Page("/Projects/Photos/Index", new { id = project.ProjectId }),
            photos = project.Photos.Select(photo => new
            {
                photo.PhotoId,
                photo.Version,
                photo.Caption,
                photo.Width,
                photo.Height,
                photo.IsCover,
                thumbnailUrl = Url.Page("/Projects/Publications/Brochure/Index", "Photo", new
                {
                    projectId = project.ProjectId,
                    photoId = photo.PhotoId,
                    mode = "thumb",
                    v = photo.Version
                }),
                previewUrl = Url.Page("/Projects/Publications/Brochure/Index", "Photo", new
                {
                    projectId = project.ProjectId,
                    photoId = photo.PhotoId,
                    mode = "source",
                    v = photo.Version
                })
            }).ToArray()
        };

    private static string NarrativeContentTab(BrochureNarrativeSource source)
        => source switch
        {
            BrochureNarrativeSource.ProjectBrief => "brief",
            BrochureNarrativeSource.CapabilityOverview => "capabilities",
            BrochureNarrativeSource.FullDescription => "description",
            _ => "brief"
        };

    private bool WantsJson()
        => string.Equals(
            Request.Headers["X-Requested-With"].ToString(),
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value, int maximumLength)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > maximumLength)
        {
            normalized = normalized[..maximumLength].TrimEnd();
        }
        return normalized;
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        bool preserveLineBreaks = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = preserveLineBreaks
            ? value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).Trim()
            : Normalize(value, maximumLength);
        if (normalized.Length > maximumLength)
        {
            normalized = normalized[..maximumLength].TrimEnd();
        }
        return normalized;
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? NormalizePhotoId(int? value)
        => value.HasValue && value.Value > 0 ? value : null;

    private static double ClampFocal(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : .5d;

    private static string SanitizeFileName(string? value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var characters = candidate
            .Select(character => invalid.Contains(character) || char.IsWhiteSpace(character) ? '_' : character)
            .ToArray();
        var normalized = string.Join(
            "_",
            new string(characters).Split('_', StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > 72)
        {
            normalized = normalized[..72].TrimEnd('_');
        }
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    public sealed class GenerateBrochureInput
    {
        [Required]
        [StringLength(120)]
        public string? Title { get; set; }

        [Required]
        [StringLength(160)]
        public string? Subtitle { get; set; }

        [Required]
        [StringLength(80)]
        public string? Edition { get; set; }

        [Required]
        [StringLength(180)]
        public string? Strapline { get; set; }

        [Required]
        public BrochureCoverStyle CoverStyle { get; set; } = BrochureCoverStyle.Institutional;

        [Required]
        public BrochureInstitutionalCoverArtwork InstitutionalCoverArtwork { get; set; } = BrochureInstitutionalCoverArtwork.ReferenceOriginal;

        [Required]
        public BrochureNarrativeSource NarrativeSource { get; set; } = BrochureNarrativeSource.ProjectBrief;

        [Required]
        public BrochurePublicationProfile PublicationProfile { get; set; } = BrochurePublicationProfile.PrintCompact;

        [StringLength(120)]
        public string? IntroductionTitle { get; set; }

        [StringLength(3000)]
        public string? IntroductionText { get; set; }

        [StringLength(5000)]
        public string? PrintIntroText { get; set; }

        [StringLength(3500)]
        public string? PrintFutureText { get; set; }

        [StringLength(3500)]
        public string? PrintProcurementText { get; set; }

        [StringLength(1200)]
        public string? PrintCentreStatement { get; set; }

        [StringLength(1800)]
        public string? PrintDevelopingAgencyText { get; set; }

        [StringLength(1200)]
        public string? PrintManufacturingAgencyText { get; set; }

        [StringLength(4500)]
        public string? PrintVisionaryText { get; set; }

        [StringLength(1800)]
        public string? PrintNewSimulatorsText { get; set; }

        [StringLength(80)]
        [Display(Name = "Handling/classification marking")]
        public string? HandlingMarking { get; set; }

        public bool AllowTextOnlyProjects { get; set; }

        public int? CoverHeroProjectId { get; set; }

        public int? CoverHeroPhotoId { get; set; }

        public double CoverHeroFocalX { get; set; } = .5d;

        public double CoverHeroFocalY { get; set; } = .5d;

        public bool CoverReviewed { get; set; }

        public bool IncludeBackCover { get; set; } = true;

        public List<BrochureProjectSelectionInput> Selections { get; set; } = new();
    }

    public sealed class BrochureProjectSelectionInput
    {
        public int ProjectId { get; set; }
        public int? PrimaryPhotoId { get; set; }
        public int? SecondaryPhotoId { get; set; }
        public double PrimaryFocalX { get; set; } = .5d;
        public double PrimaryFocalY { get; set; } = .5d;
        public double SecondaryFocalX { get; set; } = .5d;
        public double SecondaryFocalY { get; set; } = .5d;
        public BrochureImageMode ImageMode { get; set; } = BrochureImageMode.Automatic;
        public bool PrimaryPhotoConfirmed { get; set; }
        public bool IsReviewed { get; set; }
    }
}
