using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Pages.Photos;

[Authorize(Roles = "Admin,HoD")]
public sealed class FaceThumbnailModel : PageModel
{
    private readonly MediaLibraryDbContext _db;
    private readonly MediaLibraryOptions _options;
    private readonly IWebHostEnvironment _environment;

    public FaceThumbnailModel(
        MediaLibraryDbContext db,
        IOptions<MediaLibraryOptions> options,
        IWebHostEnvironment environment)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!_options.People.Enabled)
        {
            return NotFound();
        }

        var relativePath = await _db.Faces
            .AsNoTracking()
            .Where(face => face.Id == id && !face.IsSuppressed)
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

        Response.Headers.CacheControl = "private, max-age=300";
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
