using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ProjectManagement.Data;

namespace ProjectManagement.Services.DocRepo;

/// <summary>
/// The result of one check of the repository's files: which documents (newest first) have no
/// file in storage, how many were checked, whether the check stopped at the cap, and when it ran.
/// </summary>
public sealed record DocRepoMissingFilesScan(
    IReadOnlyList<Guid> MissingIds,
    int CheckedCount,
    bool Capped,
    int Limit,
    bool IncludeDeleted,
    DateTimeOffset CheckedUtc)
{
    public int MissingCount => MissingIds.Count;
}

public interface IDocRepoMissingFilesService
{
    /// <summary>
    /// Checks the newest <see cref="DocRepoMissingFilesService.CheckLimit"/> documents' files. A
    /// result younger than <see cref="DocRepoMissingFilesService.CacheDuration"/> is reused
    /// unless <paramref name="forceRefresh"/> is set. The token is observed between files.
    /// </summary>
    Task<DocRepoMissingFilesScan> ScanAsync(bool includeDeleted, bool forceRefresh = false, CancellationToken cancellationToken = default);
}

/// <summary>
/// One missing-files check shared by the Missing files page and System health's
/// "Document Repository files" row, so both report the same number. It used to be two scans:
/// the page checked every document on every load, System health the newest 5,000.
/// </summary>
public sealed class DocRepoMissingFilesService : IDocRepoMissingFilesService
{
    /// <summary>The newest documents checked; older ones are left out and the result says so.</summary>
    public const int CheckLimit = 5000;

    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    private const string CacheKeyPrefix = "docrepo-missing-files-v1:";

    private readonly ApplicationDbContext _db;
    private readonly IDocStorage _storage;
    private readonly IMemoryCache _cache;
    private readonly IClock _clock;
    private readonly int _limit;

    public DocRepoMissingFilesService(ApplicationDbContext db, IDocStorage storage, IMemoryCache cache, IClock clock)
        : this(db, storage, cache, clock, CheckLimit)
    {
    }

    internal DocRepoMissingFilesService(ApplicationDbContext db, IDocStorage storage, IMemoryCache cache, IClock clock, int limit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _limit = limit > 0 ? limit : throw new ArgumentOutOfRangeException(nameof(limit));
    }

    public async Task<DocRepoMissingFilesScan> ScanAsync(bool includeDeleted, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var key = CacheKeyPrefix + (includeDeleted ? "with-trash" : "library");
        if (!forceRefresh && _cache.TryGetValue(key, out DocRepoMissingFilesScan? cached) && cached is not null)
        {
            return cached;
        }

        var documents = await _db.Documents
            .AsNoTracking()
            .Where(d => includeDeleted || !d.IsDeleted)
            .OrderByDescending(d => d.CreatedAtUtc)
            .ThenBy(d => d.Id)
            .Select(d => new { d.Id, d.StoragePath })
            .Take(_limit + 1)
            .ToListAsync(cancellationToken);

        var capped = documents.Count > _limit;
        if (capped)
        {
            documents.RemoveAt(documents.Count - 1);
        }

        var missing = new List<Guid>();
        foreach (var document in documents)
        {
            // File.Exists does not observe the token, so stop between files: on a slow share a
            // timed-out or abandoned check ends here instead of finishing the batch.
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _storage.ExistsAsync(document.StoragePath ?? string.Empty, cancellationToken))
            {
                missing.Add(document.Id);
            }
        }

        var scan = new DocRepoMissingFilesScan(missing, documents.Count, capped, _limit, includeDeleted, _clock.UtcNow);
        _cache.Set(key, scan, CacheDuration);
        return scan;
    }
}
