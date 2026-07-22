using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace ProjectManagement.Services.ProjectBriefings;

public interface IProjectBriefingPhotoLoader
{
    Task<IReadOnlyDictionary<int, ProjectBriefingPhotoProbe>> ProbeAsync(
        IReadOnlyCollection<ProjectBriefingPhotoReference> references,
        CancellationToken cancellationToken = default);

    Task<ProjectBriefingPhotoContent?> LoadAsync(
        int projectId,
        int photoId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves and validates project photographs against the files that can actually be
/// embedded in PowerPoint. It deliberately does not equate a database photo record
/// with a presentation-ready image.
/// </summary>
public sealed class ProjectBriefingPhotoLoader : IProjectBriefingPhotoLoader
{
    private static readonly string[] PreferredSizeKeys = ["xl", "lg", "md", "sm", "xs"];
    private static readonly string[] MasterExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly ApplicationDbContext _db;
    private readonly IUploadRootProvider _uploadRoots;
    private readonly ProjectPhotoOptions _options;
    private readonly ILogger<ProjectBriefingPhotoLoader> _logger;

    public ProjectBriefingPhotoLoader(
        ApplicationDbContext db,
        IUploadRootProvider uploadRoots,
        IOptions<ProjectPhotoOptions> options,
        ILogger<ProjectBriefingPhotoLoader> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _uploadRoots = uploadRoots ?? throw new ArgumentNullException(nameof(uploadRoots));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyDictionary<int, ProjectBriefingPhotoProbe>> ProbeAsync(
        IReadOnlyCollection<ProjectBriefingPhotoReference> references,
        CancellationToken cancellationToken = default)
    {
        if (references is null || references.Count == 0)
        {
            return new Dictionary<int, ProjectBriefingPhotoProbe>();
        }

        var normalized = references
            .Where(reference => reference.ProjectId > 0 && reference.PhotoId > 0)
            .GroupBy(reference => reference.PhotoId)
            .Select(group => group.First())
            .ToArray();
        if (normalized.Length == 0)
        {
            return new Dictionary<int, ProjectBriefingPhotoProbe>();
        }

        var photoIds = normalized.Select(reference => reference.PhotoId).ToArray();
        var photos = await _db.ProjectPhotos
            .AsNoTracking()
            .Where(photo => photoIds.Contains(photo.Id))
            .ToDictionaryAsync(photo => photo.Id, cancellationToken);

        var result = new Dictionary<int, ProjectBriefingPhotoProbe>(normalized.Length);
        foreach (var reference in normalized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!photos.TryGetValue(reference.PhotoId, out var photo)
                || photo.ProjectId != reference.ProjectId)
            {
                result[reference.PhotoId] = new ProjectBriefingPhotoProbe(
                    reference.ProjectId,
                    reference.PhotoId,
                    false,
                    "The selected photograph record was not found for this project.");
                continue;
            }

            var probe = ProbePhoto(photo);
            result[reference.PhotoId] = new ProjectBriefingPhotoProbe(
                reference.ProjectId,
                reference.PhotoId,
                probe.IsReady,
                probe.FailureReason);
        }

        return result;
    }

    public async Task<ProjectBriefingPhotoContent?> LoadAsync(
        int projectId,
        int photoId,
        CancellationToken cancellationToken = default)
    {
        var photo = await _db.ProjectPhotos
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == photoId && candidate.ProjectId == projectId,
                cancellationToken);
        if (photo is null)
        {
            _logger.LogWarning(
                "Briefing photo record was not found. ProjectId={ProjectId}, PhotoId={PhotoId}",
                projectId,
                photoId);
            return null;
        }

        var attempts = new List<string>();
        foreach (var candidate in EnumerateCandidates(photo))
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts.Add(candidate.Variant);
            if (!File.Exists(candidate.Path))
            {
                continue;
            }

            try
            {
                var source = await File.ReadAllBytesAsync(candidate.Path, cancellationToken);
                using var image = Image.Load(source);
                image.Mutate(context => context
                    .AutoOrient()
                    .BackgroundColor(Color.White)
                    .Resize(new ResizeOptions
                    {
                        Size = new Size(1600, 900),
                        Mode = ResizeMode.Crop,
                        Position = AnchorPositionMode.Center
                    }));

                using var output = new MemoryStream();
                image.Save(output, new JpegEncoder { Quality = 88 });
                return new ProjectBriefingPhotoContent(
                    projectId,
                    photoId,
                    output.ToArray(),
                    "image/jpeg",
                    candidate.Variant);
            }
            catch (UnknownImageFormatException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Briefing photo candidate has an unsupported format. ProjectId={ProjectId}, PhotoId={PhotoId}, Variant={Variant}",
                    projectId,
                    photoId,
                    candidate.Variant);
            }
            catch (InvalidImageContentException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Briefing photo candidate is unreadable. ProjectId={ProjectId}, PhotoId={PhotoId}, Variant={Variant}",
                    projectId,
                    photoId,
                    candidate.Variant);
            }
            catch (IOException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Briefing photo candidate could not be read. ProjectId={ProjectId}, PhotoId={PhotoId}, Variant={Variant}",
                    projectId,
                    photoId,
                    candidate.Variant);
            }
        }

        _logger.LogWarning(
            "No PowerPoint-ready photograph was found. ProjectId={ProjectId}, PhotoId={PhotoId}, Attempts={Attempts}",
            projectId,
            photoId,
            string.Join(", ", attempts));
        return null;
    }

    private (bool IsReady, string? FailureReason) ProbePhoto(ProjectPhoto photo)
    {
        var foundFile = false;
        foreach (var candidate in EnumerateCandidates(photo))
        {
            if (!File.Exists(candidate.Path))
            {
                continue;
            }

            foundFile = true;
            try
            {
                using var stream = new FileStream(
                    candidate.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                if (Image.Identify(stream) is not null)
                {
                    return (true, null);
                }
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or UnknownImageFormatException
                                               or InvalidImageContentException)
            {
                _logger.LogDebug(
                    exception,
                    "Briefing photo probe rejected a candidate. ProjectId={ProjectId}, PhotoId={PhotoId}, Variant={Variant}",
                    photo.ProjectId,
                    photo.Id,
                    candidate.Variant);
            }
        }

        return foundFile
            ? (false, "The selected photograph exists but could not be decoded for PowerPoint.")
            : (false, "No usable photograph derivative or master image was found.");
    }

    private IEnumerable<PhotoCandidate> EnumerateCandidates(ProjectPhoto photo)
    {
        var directory = _uploadRoots.GetProjectPhotosRoot(photo.ProjectId);
        var sizeKeys = PreferredSizeKeys
            .Where(key => _options.Derivatives.ContainsKey(key))
            .Concat(_options.Derivatives
                .Where(pair => !PreferredSizeKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(pair => pair.Value.Width * pair.Value.Height)
                .Select(pair => pair.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var extensions = PreferredExtensions(photo.ContentType);
        foreach (var sizeKey in sizeKeys)
        {
            foreach (var extension in extensions)
            {
                yield return new PhotoCandidate(
                    Path.Combine(directory, $"{photo.StorageKey}-{sizeKey}{extension}"),
                    $"{sizeKey}/{extension.TrimStart('.')}");
            }
        }

        foreach (var extension in MasterExtensions)
        {
            yield return new PhotoCandidate(
                Path.Combine(directory, $"{photo.StorageKey}-master{extension}"),
                $"master/{extension.TrimStart('.')}");
        }
    }

    private static IReadOnlyList<string> PreferredExtensions(string? contentType)
    {
        var preferred = contentType?.Trim().ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };

        return new[] { preferred, ".jpg", ".jpeg", ".png", ".webp" }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record PhotoCandidate(string Path, string Variant);
}
