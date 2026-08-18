using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos.People;

/// <summary>
/// Serves only the representative crop of a visible, human-confirmed person.
/// Administrative face thumbnails remain protected by the stricter FaceThumbnail page.
/// </summary>
[Authorize]
public sealed class PortraitModel : PageModel
{
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaAssetVisibilityPolicy _visibility;
    private readonly MediaLibraryOptions _options;
    private readonly IWebHostEnvironment _environment;

    public PortraitModel(
        MediaLibraryDbContext db,
        IMediaAssetVisibilityPolicy visibility,
        IOptions<MediaLibraryOptions> options,
        IWebHostEnvironment environment)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!_options.People.Enabled || id == Guid.Empty)
        {
            return NotFound();
        }

        var visibleAssetIds = _visibility
            .Apply(_db.Assets.AsNoTracking())
            .Select(asset => asset.Id);
        var relativePath = await (
                from person in _db.Persons.AsNoTracking()
                join face in _db.Faces.AsNoTracking()
                    on person.RepresentativeFaceId equals (Guid?)face.Id
                where person.Id == id
                      && person.Status == MediaPersonStatus.Confirmed
                      && !person.IsHidden
                      && !face.IsSuppressed
                      && visibleAssetIds.Contains(face.MediaAssetId)
                      && face.PersonAssignments.Any(assignment =>
                          assignment.MediaPersonId == person.Id
                          && assignment.RemovedAtUtc == null)
                select face.ReviewThumbnailPath)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return NotFound();
        }

        var root = ResolveCacheRoot();
        var candidate = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInsideRoot(root, candidate) || !System.IO.File.Exists(candidate))
        {
            return NotFound();
        }

        Response.Headers["Cache-Control"] = "private, max-age=300";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return new PhysicalFileResult(candidate, "image/webp")
        {
            EnableRangeProcessing = false
        };
    }

    private string ResolveCacheRoot()
        => Path.GetFullPath(Path.IsPathRooted(_options.CacheRoot)
            ? _options.CacheRoot
            : Path.Combine(_environment.ContentRootPath, _options.CacheRoot));

    private static bool IsInsideRoot(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootPrefix = root.TrimEnd(
                             Path.DirectorySeparatorChar,
                             Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootPrefix, comparison);
    }
}
