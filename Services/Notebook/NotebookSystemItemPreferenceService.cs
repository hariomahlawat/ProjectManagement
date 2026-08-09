using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.ViewModels.Notebook;

namespace ProjectManagement.Services.Notebook;

/// <summary>
/// Stores only user-specific Notebook presentation state for live system surfaces.
/// Conference content is never copied into Notebook persistence.
/// </summary>
public sealed class NotebookSystemItemPreferenceService : INotebookSystemItemPreferenceService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public NotebookSystemItemPreferenceService(ApplicationDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<NotebookSystemItemPreferenceVm> GetAsync(string userId, string systemItemKey, CancellationToken ct = default)
    {
        await EnsureCommandAccessAsync(userId, systemItemKey, ct);
        var row = await PreferenceQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.SystemItemKey == systemItemKey, ct);
        return row is null ? Default(systemItemKey) : Map(row);
    }

    public async Task<NotebookSystemItemPreferenceVm> UpdateAsync(
        string userId,
        string systemItemKey,
        NotebookSystemItemPreferencePatch patch,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        await EnsureCommandAccessAsync(userId, systemItemKey, ct);

        var row = await PreferenceQuery()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.SystemItemKey == systemItemKey, ct);
        var created = row is null;
        row ??= CreateDefault(userId, systemItemKey);
        if (created) _db.NotebookSystemItemPreferences.Add(row);

        if (patch.ShowInHome.HasValue)
        {
            row.ShowInHome = patch.ShowInHome.Value;
            if (row.ShowInHome && created)
            {
                row.IsPinned = false;
                row.HomePosition = 0;
            }
        }

        if (patch.IsPinned.HasValue)
        {
            row.IsPinned = patch.IsPinned.Value;
            row.HomePosition = 0;
            if (row.IsPinned) row.ShowInHome = true;
        }

        if (patch.ColorKey is not null)
        {
            if (!NotebookRules.IsAllowedColour(patch.ColorKey))
            {
                throw new NotebookValidationException("Unsupported colour.");
            }

            row.ColorKey = NotebookRules.CleanColour(patch.ColorKey);
        }

        if (patch.Labels is not null)
        {
            await SyncLabelsAsync(row, userId, patch.Labels, ct);
        }

        Touch(row);
        await _db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<NotebookSystemItemPreferenceVm> SetPlacementAsync(
        string userId,
        string systemItemKey,
        bool isPinned,
        int position,
        CancellationToken ct = default)
    {
        await EnsureCommandAccessAsync(userId, systemItemKey, ct);
        var row = await PreferenceQuery()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.SystemItemKey == systemItemKey, ct);
        row ??= CreateDefault(userId, systemItemKey);
        if (_db.Entry(row).State == EntityState.Detached) _db.NotebookSystemItemPreferences.Add(row);

        var ownedCount = await _db.NotebookItems.AsNoTracking().CountAsync(item =>
            item.OwnerId == userId &&
            item.DeletedAtUtc == null &&
            item.Status == NotebookItemStatus.Active &&
            item.IsPinned == isPinned,
            ct);

        row.ShowInHome = true;
        row.IsPinned = isPinned;
        row.HomePosition = Math.Clamp(position, 0, ownedCount);
        Touch(row);
        await _db.SaveChangesAsync(ct);
        return Map(row);
    }

    private IQueryable<NotebookSystemItemPreference> PreferenceQuery()
        => _db.NotebookSystemItemPreferences
            .Include(x => x.Tags)
            .ThenInclude(x => x.NotebookTag);

    private async Task SyncLabelsAsync(
        NotebookSystemItemPreference row,
        string userId,
        IReadOnlyList<string> labels,
        CancellationToken ct)
    {
        var clean = NormaliseLabels(labels);
        if (clean.Count > NotebookLimits.MaxLabelsPerItem)
        {
            throw new NotebookValidationException($"A note can have at most {NotebookLimits.MaxLabelsPerItem} labels.");
        }

        var requested = clean.ToDictionary(x => x.ToUpperInvariant(), x => x, StringComparer.Ordinal);
        var normalized = requested.Keys.ToArray();
        var tags = await _db.NotebookTags
            .Where(x => x.OwnerId == userId && normalized.Contains(x.NormalizedName))
            .ToListAsync(ct);

        var byNormalized = tags.ToDictionary(x => x.NormalizedName, StringComparer.Ordinal);
        foreach (var pair in requested)
        {
            if (byNormalized.ContainsKey(pair.Key)) continue;
            var tag = new NotebookTag
            {
                OwnerId = userId,
                Name = pair.Value,
                NormalizedName = pair.Key
            };
            _db.NotebookTags.Add(tag);
            tags.Add(tag);
            byNormalized[pair.Key] = tag;
        }

        row.Tags.Clear();
        foreach (var tag in tags.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            row.Tags.Add(new NotebookSystemItemTag
            {
                Preference = row,
                NotebookTag = tag
            });
        }
    }

    private static IReadOnlyList<string> NormaliseLabels(IReadOnlyList<string>? labels)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in labels ?? Array.Empty<string>())
        {
            var value = (raw ?? string.Empty).Trim().TrimStart('#').Trim();
            if (value.Length == 0) continue;
            if (value.Length > NotebookLimits.LabelNameMaxLength)
            {
                throw new NotebookValidationException($"Label name cannot exceed {NotebookLimits.LabelNameMaxLength} characters.");
            }
            if (seen.Add(value)) result.Add(value);
        }
        return result;
    }

    private async Task EnsureCommandAccessAsync(string userId, string systemItemKey, CancellationToken ct)
    {
        if (!string.Equals(systemItemKey, NotebookSystemItemKeys.ConferenceDirections, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException("The system note could not be found.");
        }

        var roleNames = new[]
        {
            RoleNames.Comdt.ToUpperInvariant(),
            RoleNames.HoD.ToUpperInvariant()
        };

        var authorised = await (
            from userRole in _db.UserRoles.AsNoTracking()
            join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == userId && role.NormalizedName != null && roleNames.Contains(role.NormalizedName)
            select userRole.UserId)
            .AnyAsync(ct);

        if (!authorised) throw new UnauthorizedAccessException("This system note is available only to authorised command users.");
    }

    private NotebookSystemItemPreference CreateDefault(string userId, string systemItemKey) => new()
    {
        UserId = userId,
        SystemItemKey = systemItemKey,
        ShowInHome = false,
        IsPinned = false,
        HomePosition = 0,
        ColorKey = "white",
        UpdatedAtUtc = _clock.UtcNow,
        Version = Guid.NewGuid()
    };

    private static NotebookSystemItemPreferenceVm Default(string systemItemKey) => new()
    {
        SystemItemKey = systemItemKey,
        ShowInHome = false,
        IsPinned = false,
        HomePosition = 0,
        ColorKey = "white",
        Labels = Array.Empty<string>(),
        Version = Guid.Empty
    };

    private void Touch(NotebookSystemItemPreference row)
    {
        row.UpdatedAtUtc = _clock.UtcNow;
        row.Version = Guid.NewGuid();
    }

    private static NotebookSystemItemPreferenceVm Map(NotebookSystemItemPreference row) => new()
    {
        SystemItemKey = row.SystemItemKey,
        ShowInHome = row.ShowInHome,
        IsPinned = row.IsPinned,
        HomePosition = row.HomePosition,
        ColorKey = NotebookRules.CleanColour(row.ColorKey),
        Labels = row.Tags
            .Select(x => x.NotebookTag?.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray(),
        Version = row.Version
    };
}
