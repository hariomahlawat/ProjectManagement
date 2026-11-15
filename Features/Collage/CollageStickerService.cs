using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Imaging;
using ProjectManagement.Services.Storage;
using SkiaSharp;

namespace ProjectManagement.Features.Collage;

/// <summary>What a change to the stickers came to: done, or the reason in plain words.</summary>
public sealed record CollageStickerOutcome(bool Succeeded, string Message, CollageSticker? Sticker = null)
{
    public static CollageStickerOutcome Done(string message, CollageSticker? sticker = null) => new(true, message, sticker);

    public static CollageStickerOutcome Refused(string message) => new(false, message);
}

/// <summary>
/// The "Crests and logos" stickers of Collage Studio: uploading, naming, ordering, retiring and
/// restoring them, and finding their pictures. See docs/ux/collage-studio-library-and-unit-stickers.md.
/// </summary>
public interface ICollageStickerService
{
    /// <summary>The stickers shown in the picker, in order.</summary>
    Task<IReadOnlyList<CollageSticker>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Every sticker, the active ones first and then the retired ones, for administration.</summary>
    Task<IReadOnlyList<CollageSticker>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<CollageSticker?> FindAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>The absolute path of a sticker's PNG, or null when the file is missing.</summary>
    string? PicturePathOf(CollageSticker sticker);

    Task<CollageStickerOutcome> AddAsync(Stream content, string fileName, string name, string? searchWords, string? actorUserId, CancellationToken cancellationToken = default);

    Task<CollageStickerOutcome> RenameAsync(int id, string name, string? searchWords, string? actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Moves an active sticker one place earlier (<paramref name="up"/>) or later.</summary>
    Task<CollageStickerOutcome> MoveAsync(int id, bool up, string? actorUserId, CancellationToken cancellationToken = default);

    Task<CollageStickerOutcome> RetireAsync(int id, string? actorUserId, CancellationToken cancellationToken = default);

    Task<CollageStickerOutcome> RestoreAsync(int id, string? actorUserId, CancellationToken cancellationToken = default);
}

public sealed class CollageStickerService : ICollageStickerService
{
    /// <summary>The folder under the upload root that holds the sticker pictures.</summary>
    public const string Folder = "collage-stickers";

    public const long MaxFileBytes = 5_000_000;
    public const int MaxActive = 60;
    public const int MaxSide = 1024;

    // Enough for a large scanned crest; a bigger picture is refused before it is decoded.
    private const long MaxDecodePixels = 40_000_000;

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp"
    };

    private const string AuditOrigin = "Admin.CollageStickers";
    private const string LockKey = "collage-stickers";

    private readonly ApplicationDbContext _db;
    private readonly IUploadRootProvider _uploadRoot;
    private readonly IUploadPathResolver _paths;
    private readonly IAdminAuditService _audit;
    private readonly IClock _clock;
    private readonly IVirusScanner? _virusScanner;

    public CollageStickerService(
        ApplicationDbContext db,
        IUploadRootProvider uploadRoot,
        IUploadPathResolver paths,
        IAdminAuditService audit,
        IClock clock,
        IVirusScanner? virusScanner = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _uploadRoot = uploadRoot ?? throw new ArgumentNullException(nameof(uploadRoot));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _virusScanner = virusScanner;
    }

    public async Task<IReadOnlyList<CollageSticker>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await _db.CollageStickers.AsNoTracking()
            .Where(s => !s.IsRetired)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CollageSticker>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await _db.CollageStickers.AsNoTracking()
            .OrderBy(s => s.IsRetired).ThenBy(s => s.SortOrder).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

    public Task<CollageSticker?> FindAsync(int id, CancellationToken cancellationToken = default) =>
        _db.CollageStickers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public string? PicturePathOf(CollageSticker sticker)
    {
        ArgumentNullException.ThrowIfNull(sticker);
        try
        {
            var path = _paths.ToAbsolute(sticker.StorageKey);
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    public async Task<CollageStickerOutcome> AddAsync(
        Stream content,
        string fileName,
        string name,
        string? searchWords,
        string? actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var cleanName = CleanName(name);
        if (cleanName is null) return CollageStickerOutcome.Refused($"Give the sticker a name of up to {CollageSticker.NameMaxLength} letters.");
        var words = CleanWords(searchWords);
        if (words.Length > CollageSticker.SearchWordsMaxLength) return CollageStickerOutcome.Refused($"Keep the search words to {CollageSticker.SearchWordsMaxLength} letters.");

        var active = await _db.CollageStickers.CountAsync(s => !s.IsRetired, cancellationToken);
        if (active >= MaxActive) return CollageStickerOutcome.Refused($"There are already {MaxActive} stickers in use. Retire one first.");

        // Read at most one byte more than allowed, so a large upload is refused without holding it.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxFileBytes) return CollageStickerOutcome.Refused("The picture is larger than 5 MB. Choose a smaller one.");
        }
        if (buffer.Length == 0) return CollageStickerOutcome.Refused("Choose a picture to upload.");

        var bytes = new ReadOnlyMemory<byte>(buffer.GetBuffer(), 0, (int)buffer.Length);
        var info = PrismImageCodec.Identify(bytes);
        if (info is null || !AllowedTypes.Contains(info.MimeType))
        {
            return CollageStickerOutcome.Refused("Use a PNG, JPEG or WebP picture. A PNG with a clear background looks best.");
        }
        if ((long)info.Width * info.Height > MaxDecodePixels)
        {
            return CollageStickerOutcome.Refused("The picture has too many pixels. Choose a smaller one.");
        }

        if (_virusScanner is not null)
        {
            buffer.Position = 0;
            await _virusScanner.ScanAsync(buffer, fileName ?? "sticker", cancellationToken);
        }

        // The stored picture is drawn afresh from the pixels: nothing the uploader sent is kept.
        byte[] png;
        int width;
        int height;
        try
        {
            using var decoded = PrismImageCodec.DecodeOriented(bytes, MaxDecodePixels);
            using var sized = decoded.Width > MaxSide || decoded.Height > MaxSide
                ? PrismImageCodec.ResizeToFit(decoded, MaxSide, MaxSide)
                : decoded.Copy();
            png = PrismImageCodec.EncodePng(sized);
            width = sized.Width;
            height = sized.Height;
        }
        catch (PrismImageException)
        {
            return CollageStickerOutcome.Refused("The picture could not be read. Try saving it again as a PNG.");
        }

        var folder = Path.Combine(_uploadRoot.RootPath, Folder);
        Directory.CreateDirectory(folder);
        var fileStem = Guid.NewGuid().ToString("N");
        var finalPath = Path.Combine(folder, fileStem + ".png");
        var tempPath = Path.Combine(folder, fileStem + ".tmp");
        try
        {
            await File.WriteAllBytesAsync(tempPath, png, cancellationToken);
            File.Move(tempPath, finalPath);
        }
        catch
        {
            // Cancelled, disk full or the move refused: nothing half-written is left behind.
            TryDelete(tempPath);
            TryDelete(finalPath);
            throw;
        }

        CollageStickerOutcome outcome;
        try
        {
            outcome = await ChangeAsync(async () =>
            {
                // Counted again under the lock: the count above only saved decoding a refused upload.
                if (await _db.CollageStickers.CountAsync(s => !s.IsRetired, cancellationToken) >= MaxActive)
                {
                    return CollageStickerOutcome.Refused($"There are already {MaxActive} stickers in use. Retire one first.");
                }

                var now = _clock.UtcNow;
                var sticker = new CollageSticker
                {
                    Name = cleanName,
                    SearchWords = words.Length == 0 ? null : words,
                    StorageKey = $"{Folder}/{fileStem}.png",
                    Width = width,
                    Height = height,
                    Version = 1,
                    SortOrder = await NextSortOrderAsync(cancellationToken),
                    CreatedByUserId = actorUserId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                _db.CollageStickers.Add(sticker);
                await _db.SaveChangesAsync(cancellationToken);
                await RecordAsync("CollageStickerAdded", sticker, null, actorUserId, $"Added the collage sticker '{sticker.Name}'.", cancellationToken);
                return CollageStickerOutcome.Done($"Added '{sticker.Name}'.", sticker);
            }, cancellationToken);
        }
        catch
        {
            // Nothing was saved, so the picture has nothing pointing at it.
            TryDelete(finalPath);
            throw;
        }

        if (!outcome.Succeeded) TryDelete(finalPath);
        return outcome;
    }

    public async Task<CollageStickerOutcome> RenameAsync(int id, string name, string? searchWords, string? actorUserId, CancellationToken cancellationToken = default)
    {
        var cleanName = CleanName(name);
        if (cleanName is null) return CollageStickerOutcome.Refused($"Give the sticker a name of up to {CollageSticker.NameMaxLength} letters.");
        var words = CleanWords(searchWords);
        if (words.Length > CollageSticker.SearchWordsMaxLength) return CollageStickerOutcome.Refused($"Keep the search words to {CollageSticker.SearchWordsMaxLength} letters.");

        return await ChangeAsync(async () =>
        {
            var sticker = await _db.CollageStickers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (sticker is null) return CollageStickerOutcome.Refused("That sticker is no longer there.");
            var before = Snapshot(sticker);
            var oldName = sticker.Name;
            sticker.Name = cleanName;
            sticker.SearchWords = words.Length == 0 ? null : words;
            sticker.UpdatedAtUtc = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await RecordAsync("CollageStickerUpdated", sticker, before, actorUserId, $"Renamed the collage sticker '{oldName}' to '{sticker.Name}'.", cancellationToken);
            return CollageStickerOutcome.Done("Saved.", sticker);
        }, cancellationToken);
    }

    public Task<CollageStickerOutcome> MoveAsync(int id, bool up, string? actorUserId, CancellationToken cancellationToken = default) =>
        ChangeAsync(async () =>
        {
            var active = await _db.CollageStickers
                .Where(s => !s.IsRetired)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .ToListAsync(cancellationToken);
            var index = active.FindIndex(s => s.Id == id);
            if (index < 0) return CollageStickerOutcome.Refused("That sticker is no longer in the picker.");
            var other = up ? index - 1 : index + 1;
            if (other < 0 || other >= active.Count) return CollageStickerOutcome.Done("It is already there.", active[index]);

            (active[index], active[other]) = (active[other], active[index]);
            // Numbered afresh, so equal or missing numbers from older rows cannot keep two in one place.
            var now = _clock.UtcNow;
            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].SortOrder == i + 1) continue;
                active[i].SortOrder = i + 1;
                active[i].UpdatedAtUtc = now;
            }
            await _db.SaveChangesAsync(cancellationToken);
            var moved = active[other];
            await RecordAsync("CollageStickerUpdated", moved, null, actorUserId, $"Moved the collage sticker '{moved.Name}' {(up ? "earlier" : "later")} in the picker.", cancellationToken);
            return CollageStickerOutcome.Done("Moved.", moved);
        }, cancellationToken);

    public Task<CollageStickerOutcome> RetireAsync(int id, string? actorUserId, CancellationToken cancellationToken = default) =>
        ChangeAsync(async () =>
        {
            var sticker = await _db.CollageStickers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (sticker is null) return CollageStickerOutcome.Refused("That sticker is no longer there.");
            if (sticker.IsRetired) return CollageStickerOutcome.Done("It is already retired.", sticker);
            sticker.IsRetired = true;
            sticker.UpdatedAtUtc = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await RecordAsync("CollageStickerRetired", sticker, null, actorUserId, $"Retired the collage sticker '{sticker.Name}'. Collages that use it keep it.", cancellationToken);
            return CollageStickerOutcome.Done($"Retired '{sticker.Name}'. Collages that already use it keep it.", sticker);
        }, cancellationToken);

    public Task<CollageStickerOutcome> RestoreAsync(int id, string? actorUserId, CancellationToken cancellationToken = default) =>
        ChangeAsync(async () =>
        {
            var sticker = await _db.CollageStickers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (sticker is null) return CollageStickerOutcome.Refused("That sticker is no longer there.");
            if (!sticker.IsRetired) return CollageStickerOutcome.Done("It is already in the picker.", sticker);
            if (await _db.CollageStickers.CountAsync(s => !s.IsRetired, cancellationToken) >= MaxActive)
            {
                return CollageStickerOutcome.Refused($"There are already {MaxActive} stickers in use. Retire one first.");
            }
            sticker.IsRetired = false;
            sticker.SortOrder = await NextSortOrderAsync(cancellationToken);
            sticker.UpdatedAtUtc = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await RecordAsync("CollageStickerRestored", sticker, null, actorUserId, $"Put the collage sticker '{sticker.Name}' back in the picker.", cancellationToken);
            return CollageStickerOutcome.Done($"'{sticker.Name}' is back in the picker, at the end.", sticker);
        }, cancellationToken);

    /// <summary>
    /// Runs one change to the stickers and its audit entry as one database transaction, so a
    /// change is never kept without its entry (the audit service saves through the same context).
    /// On PostgreSQL every change first takes one transaction-scoped advisory lock, so two
    /// administrators cannot both find room for the sixtieth sticker or renumber the picker at once.
    /// A refused change has written nothing; a failure rolls everything back.
    /// </summary>
    private async Task<CollageStickerOutcome> ChangeAsync(Func<Task<CollageStickerOutcome>> change, CancellationToken cancellationToken)
    {
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            if (_db.Database.IsNpgsql())
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({LockKey}))", cancellationToken);
            }

            var outcome = await change();
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return outcome;
        }
        catch
        {
            // Rolled back: forget the unsaved edits so this request cannot save them later.
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<int> NextSortOrderAsync(CancellationToken cancellationToken) =>
        (await _db.CollageStickers.Where(s => !s.IsRetired).MaxAsync(s => (int?)s.SortOrder, cancellationToken) ?? 0) + 1;

    /// <summary>The name with spaces tidied, or null when it is empty or too long.</summary>
    internal static string? CleanName(string? name)
    {
        var clean = string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length == 0 || clean.Length > CollageSticker.NameMaxLength ? null : clean;
    }

    internal static string CleanWords(string? words) =>
        string.Join(' ', (words ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static object Snapshot(CollageSticker sticker) => new { sticker.Name, sticker.SearchWords, sticker.SortOrder, sticker.IsRetired };

    private Task RecordAsync(string action, CollageSticker sticker, object? before, string? actorUserId, string message, CancellationToken cancellationToken) =>
        _audit.RecordAsync(new AdminAuditEntry(
            action,
            nameof(CollageSticker),
            sticker.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Before: before,
            After: Snapshot(sticker),
            ActorUserId: actorUserId,
            Origin: AuditOrigin,
            Message: message), cancellationToken);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
