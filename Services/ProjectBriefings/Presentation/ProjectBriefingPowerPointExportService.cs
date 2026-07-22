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
        var data = await _dataService.BuildPresentationDataAsync(deckId, requestingUserId, cancellationToken);
        if (data.PresentationMode is ProjectBriefingPresentationMode.DetailedProjects
            or ProjectBriefingPresentationMode.Combined)
        {
            await AttachPhotosAsync(data, cancellationToken);
        }

        var (content, slideCount) = _composer.Compose(data);
        await _deckService.MarkGeneratedAsync(deckId, requestingUserId, slideCount, cancellationToken);

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

    private async Task AttachPhotosAsync(
        ProjectBriefingPresentationData data,
        CancellationToken cancellationToken)
    {
        foreach (var project in data.Projects)
        {
            if (!project.CoverPhotoId.HasValue)
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
