using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos;

/// <summary>
/// Face crops for identity review. Admin and HoD may view any visible face. A staff member
/// linked to a person may view only the faces currently suggested as that person, which is
/// what "Find more photos of me" asks them to confirm or reject.
/// </summary>
[Authorize]
public sealed class FaceThumbnailModel : PageModel
{
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaAssetVisibilityPolicy _visibility;
    private readonly IMediaPersonUserLinkService _personUserLinks;
    private readonly IPersonPhotoDiscoveryQueryService _personDiscovery;
    private readonly MediaLibraryOptions _options;
    private readonly IWebHostEnvironment _environment;

    public FaceThumbnailModel(
        MediaLibraryDbContext db,
        IMediaAssetVisibilityPolicy visibility,
        IMediaPersonUserLinkService personUserLinks,
        IPersonPhotoDiscoveryQueryService personDiscovery,
        IOptions<MediaLibraryOptions> options,
        IWebHostEnvironment environment)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
        _personUserLinks = personUserLinks ?? throw new ArgumentNullException(nameof(personUserLinks));
        _personDiscovery = personDiscovery ?? throw new ArgumentNullException(nameof(personDiscovery));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public async Task<IActionResult> OnGetAsync(Guid id, Guid? personId, CancellationToken cancellationToken)
    {
        if (!_options.People.Enabled)
        {
            return NotFound();
        }

        if (!User.IsInRole("Admin") && !User.IsInRole("HoD")
            && !await IsCurrentCandidateForLinkedSelfAsync(id, personId, cancellationToken))
        {
            return Forbid();
        }

        var visibleAssetIds = _visibility
            .Apply(_db.Assets.AsNoTracking())
            .Select(asset => asset.Id);
        var relativePath = await _db.Faces
            .AsNoTracking()
            .Where(face => face.Id == id
                           && !face.IsSuppressed
                           && visibleAssetIds.Contains(face.MediaAssetId))
            .Select(face => face.ReviewThumbnailPath)
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

    /// <summary>
    /// True when the signed-in user is linked to <paramref name="personId"/> (with no open
    /// concern on the link) and the face is one of that person's current suggestions.
    /// </summary>
    private async Task<bool> IsCurrentCandidateForLinkedSelfAsync(
        Guid faceId,
        Guid? personId,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (personId is not { } requestedPerson || requestedPerson == Guid.Empty || string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var link = await _personUserLinks.TryGetPhotoIdentityForUserAsync(userId, cancellationToken);
        if (link is null || link.PersonId != requestedPerson || link.HasOpenConcern)
        {
            return false;
        }

        var eligible = await _personDiscovery.GetEligibleCandidatesAsync(
            requestedPerson,
            new[] { faceId },
            cancellationToken);
        return eligible.ContainsKey(faceId);
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
