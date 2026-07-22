using System.Text.RegularExpressions;
using ProjectManagement.Utilities;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.Projects;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingPowerPointExportService : IProjectBriefingPowerPointExportService
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    private readonly IProjectBriefingDataService _dataService;
    private readonly IProjectBriefingDeckService _deckService;
    private readonly IProjectBriefingSlideComposer _composer;
    private readonly IProjectPhotoService _projectPhotoService;
    private readonly ILogger<ProjectBriefingPowerPointExportService> _logger;

    public ProjectBriefingPowerPointExportService(
        IProjectBriefingDataService dataService,
        IProjectBriefingDeckService deckService,
        IProjectBriefingSlideComposer composer,
        IProjectPhotoService projectPhotoService,
        ILogger<ProjectBriefingPowerPointExportService> logger)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _deckService = deckService ?? throw new ArgumentNullException(nameof(deckService));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _projectPhotoService = projectPhotoService ?? throw new ArgumentNullException(nameof(projectPhotoService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProjectBriefingExportResult> GenerateAsync(
        long deckId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var data = await _dataService.BuildPresentationDataAsync(deckId, ownerUserId, cancellationToken);
        if (data.PresentationMode is ProjectBriefingPresentationMode.DetailedProjects
            or ProjectBriefingPresentationMode.Combined)
        {
            await AttachPhotosAsync(data, cancellationToken);
        }

        var (content, slideCount) = _composer.Compose(data);
        await _deckService.MarkGeneratedAsync(deckId, ownerUserId, slideCount, cancellationToken);

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
                var opened = await _projectPhotoService.OpenDerivativeAsync(
                    project.ProjectId,
                    project.CoverPhotoId.Value,
                    "xl",
                    "jpg",
                    cancellationToken);

                opened ??= await _projectPhotoService.OpenDerivativeAsync(
                    project.ProjectId,
                    project.CoverPhotoId.Value,
                    "xl",
                    "png",
                    cancellationToken);

                opened ??= await _projectPhotoService.OpenDerivativeAsync(
                    project.ProjectId,
                    project.CoverPhotoId.Value,
                    "md",
                    preferWebp: false,
                    cancellationToken);

                if (opened is null)
                {
                    _logger.LogWarning(
                        "No presentation-compatible derivative was found for project {ProjectId}, photo {PhotoId}.",
                        project.ProjectId,
                        project.CoverPhotoId.Value);
                    continue;
                }

                await using var stream = opened.Value.Stream;
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, cancellationToken);
                project.CoverPhoto = NormalizePhotoForSlide(memory.ToArray());
                project.CoverPhotoContentType = "image/jpeg";
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


    private static byte[] NormalizePhotoForSlide(byte[] source)
    {
        using var image = Image.Load(source);
        image.Mutate(context => context
            .AutoOrient()
            .Resize(new ResizeOptions
            {
                Size = new Size(1600, 1048),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center
            }));

        using var output = new MemoryStream();
        image.Save(output, new JpegEncoder { Quality = 88 });
        return output.ToArray();
    }

    [GeneratedRegex(@"[^A-Za-z0-9._-]+", RegexOptions.Compiled)]
    private static partial Regex FileNameSanitizer();
}
