using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos;

[Authorize]
public sealed class DownloadModel : PageModel
{
    private const int MaximumSelection = 120;
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaAssetVisibilityPolicy _visibility;
    private readonly IMediaContentProviderResolver _contentResolver;
    private readonly ILogger<DownloadModel> _logger;

    public DownloadModel(
        MediaLibraryDbContext db,
        IMediaAssetVisibilityPolicy visibility,
        IMediaContentProviderResolver contentResolver,
        ILogger<DownloadModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
        _contentResolver = contentResolver ?? throw new ArgumentNullException(nameof(contentResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IActionResult> OnPostAsync(long[]? assetIds, CancellationToken cancellationToken)
    {
        var requestedIds = (assetIds ?? Array.Empty<long>())
            .Where(id => id > 0)
            .Distinct()
            .Take(MaximumSelection + 1)
            .ToArray();

        if (requestedIds.Length == 0)
        {
            return BadRequest("Select at least one catalogue-backed media item.");
        }

        if (requestedIds.Length > MaximumSelection)
        {
            return BadRequest($"A maximum of {MaximumSelection} media items can be downloaded at once.");
        }

        var assets = await _visibility
            .Apply(_db.Assets.AsNoTracking().Include(asset => asset.Source))
            .Where(asset => requestedIds.Contains(asset.Id))
            .OrderBy(asset => asset.MediaDateUtc)
            .ThenBy(asset => asset.Id)
            .ToListAsync(cancellationToken);

        if (assets.Count == 0)
        {
            return NotFound("The selected media is no longer available.");
        }

        var resolved = new List<(long AssetId, MediaContentDescriptor Content)>(assets.Count);
        foreach (var asset in assets)
        {
            try
            {
                var content = await _contentResolver.ResolveAsync(asset, cancellationToken);
                if (content is not null)
                {
                    resolved.Add((asset.Id, content));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // One stale physical file must not invalidate an otherwise valid bulk download.
                _logger.LogWarning(exception, "Unable to resolve media asset {AssetId} for a bulk Photos download.", asset.Id);
            }
        }

        if (resolved.Count == 0)
        {
            return NotFound("None of the selected media files could be opened.");
        }

        var archiveName = $"PRISM_Photos_{DateTime.Now:yyyyMMdd_HHmm}.zip";
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/zip";
        Response.Headers["Content-Disposition"] = $"attachment; filename=\"{archiveName}\"";
        Response.Headers["Cache-Control"] = "no-store, no-cache";

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var archive = new ZipArchive(Response.Body, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            foreach (var item in resolved)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryName = MakeUniqueArchiveName(item.Content.FileName, item.AssetId, usedNames);
                try
                {
                    await using var source = await item.Content.OpenReadAsync(cancellationToken);
                    var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                    await using var target = entry.Open();
                    await source.CopyToAsync(target, 128 * 1024, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Media asset {AssetId} became unavailable while the bulk archive was being written.", item.AssetId);
                    // A source that disappears before its entry is created is skipped. If it
                    // fails mid-copy, ZipArchive may retain a partial entry; do not terminate the
                    // otherwise valid archive stream for one stale physical file.
                }
            }
        }

        return new EmptyResult();
    }

    private static string MakeUniqueArchiveName(string? preferredName, long assetId, ISet<string> usedNames)
    {
        var candidate = Path.GetFileName(string.IsNullOrWhiteSpace(preferredName)
            ? $"media-{assetId}"
            : preferredName.Trim());

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            candidate = candidate.Replace(invalid, '_');
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = $"media-{assetId}";
        }

        if (usedNames.Add(candidate))
        {
            return candidate;
        }

        var extension = Path.GetExtension(candidate);
        var stem = Path.GetFileNameWithoutExtension(candidate);
        for (var suffix = 2; suffix < 10_000; suffix++)
        {
            var unique = $"{stem} ({suffix}){extension}";
            if (usedNames.Add(unique))
            {
                return unique;
            }
        }

        var fallback = $"{stem}-{assetId}{extension}";
        usedNames.Add(fallback);
        return fallback;
    }
}
