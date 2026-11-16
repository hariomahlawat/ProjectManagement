using System.Text.RegularExpressions;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingPowerPointExportService : IProjectBriefingPowerPointExportService
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    private readonly IProjectBriefingDataService _dataService;
    private readonly IProjectBriefingDeckService _deckService;
    private readonly IProjectBriefingSlideComposer _composer;
    private readonly IProjectBriefingPhotoLoader _photoLoader;
    private readonly ILogger<ProjectBriefingPowerPointExportService> _logger;

    public ProjectBriefingPowerPointExportService(
        IProjectBriefingDataService dataService,
        IProjectBriefingDeckService deckService,
        IProjectBriefingSlideComposer composer,
        IProjectBriefingPhotoLoader photoLoader,
        ILogger<ProjectBriefingPowerPointExportService> logger)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _deckService = deckService ?? throw new ArgumentNullException(nameof(deckService));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _photoLoader = photoLoader ?? throw new ArgumentNullException(nameof(photoLoader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProjectBriefingExportResult> GenerateAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default)
    {
        // Generating records the PowerPoint on the deck, so it is for someone who may change it;
        // checked first so a view-only request does not build the slides for nothing.
        var access = await _deckService.GetAccessAsync(deckId, requestingUserId, cancellationToken);
        if (access != ProjectBriefingDeckAccessLevel.Edit)
        {
            throw access == ProjectBriefingDeckAccessLevel.View
                ? new InvalidOperationException(ProjectBriefingDeckService.ViewOnlyMessage)
                : new KeyNotFoundException(ProjectBriefingDeckService.NotFoundMessage);
        }

        var (data, content, slideCount, _) = await ComposeAsync(deckId, requestingUserId, cancellationToken);

        // What these slides say about each project, so the library can tell when the data moves.
        var slideFacts = ProjectBriefingSlideFacts.Write(data);
        await _deckService.MarkGeneratedAsync(deckId, requestingUserId, slideCount, cancellationToken, slideFacts);
        return Package(data, content, slideCount);
    }

    public async Task<ProjectBriefingExportResult> DownloadAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default)
    {
        // The deck's data refuses anyone who may not see it (ProjectBriefingDataService).
        var (data, content, slideCount, _) = await ComposeAsync(deckId, requestingUserId, cancellationToken);
        return Package(data, content, slideCount);
    }

    private static ProjectBriefingExportResult Package(
        ProjectBriefingPresentationData data,
        byte[] content,
        int slideCount)
    {
        var date = TimeZoneInfo.ConvertTime(data.GeneratedAtUtc, TimeZoneHelper.GetIst()).ToString("yyyyMMdd");
        var safeName = FileNameSanitizer().Replace(data.DeckName, "_").Trim('_');
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "Project_Briefing_Deck";
        }

        return new ProjectBriefingExportResult(
            content,
            ContentType,
            $"{safeName}_{date}.pptx",
            slideCount);
    }

    public async Task<ProjectBriefingComposedDeck> ComposeAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var data = await _dataService.BuildPresentationDataAsync(deckId, requestingUserId, cancellationToken);
        if (data.Layout == ProjectBriefingLayout.ProjectUpdateSheet
            || data.PresentationMode is ProjectBriefingPresentationMode.DetailedProjects
                or ProjectBriefingPresentationMode.Combined)
        {
            await AttachPhotosAsync(data, cancellationToken);
        }

        try
        {
            var composition = _composer.ComposeWithOutline(data);
            return new ProjectBriefingComposedDeck(data, composition.Content, composition.SlideCount, composition.Outline);
        }
        catch (ProjectBriefingPresentationIntegrityException exception)
        {
            _logger.LogError(
                exception,
                "Generated briefing deck failed PowerPoint integrity validation. "
                + "DeckId={DeckId}, IssueCount={IssueCount}, Issues={IntegrityIssues}",
                deckId,
                exception.Issues.Count,
                string.Join(" || ", exception.Issues));
            throw;
        }
    }

    private async Task AttachPhotosAsync(
        ProjectBriefingPresentationData data,
        CancellationToken cancellationToken)
    {
        foreach (var project in data.Projects)
        {
            // A table row shows no photograph.
            if (!project.CoverPhotoId.HasValue || project.TableRowOnly)
            {
                continue;
            }

            try
            {
                var loaded = await _photoLoader.LoadAsync(
                    project.ProjectId,
                    project.CoverPhotoId.Value,
                    cancellationToken);
                if (loaded is null)
                {
                    continue;
                }

                project.CoverPhoto = loaded.Content;
                project.CoverPhotoContentType = loaded.ContentType;
                _logger.LogDebug(
                    "Attached briefing photograph. DeckId={DeckId}, ProjectId={ProjectId}, PhotoId={PhotoId}, Variant={Variant}",
                    data.DeckId,
                    project.ProjectId,
                    project.CoverPhotoId.Value,
                    loaded.SourceVariant);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Unable to load the cover photograph for project {ProjectId} while generating briefing deck {DeckId}.",
                    project.ProjectId,
                    data.DeckId);
            }
        }
    }

    [GeneratedRegex(@"[^A-Za-z0-9._-]+", RegexOptions.Compiled)]
    private static partial Regex FileNameSanitizer();
}
