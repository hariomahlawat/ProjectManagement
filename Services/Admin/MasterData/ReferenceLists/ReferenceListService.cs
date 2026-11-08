using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Services.Search;

namespace ProjectManagement.Services.Admin.MasterData.ReferenceLists;

public static class ReferenceListStatuses
{
    public const string All = "all";
    public const string InUse = "inuse";
    public const string Active = "active";
    public const string Inactive = "inactive";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        InUse => InUse,
        Active => Active,
        Inactive => Inactive,
        _ => All
    };
}

public sealed record ReferenceListRequest(string? Search, string? Status, int Page, int PageSize);

/// <summary>The tiles: every item, items something uses, and inactive items.</summary>
public sealed record ReferenceListTiles(int All, int InUse, int Inactive);

public sealed record ReferenceListUsage(string One, string Many, int Count)
{
    public string Text => $"{Count} {(Count == 1 ? One : Many)}";
}

public sealed record ReferenceListRow(
    int Id,
    string Name,
    int SortOrder,
    bool IsActive,
    string RowVersion,
    IReadOnlyList<ReferenceListUsage> UsedBy,
    bool CanMoveUp,
    bool CanMoveDown,
    IReadOnlyList<string?> ExtraValues)
{
    public int UsageCount => UsedBy.Sum(item => item.Count);

    /// <summary>Delete is offered only when nothing uses the item.</summary>
    public bool CanDelete => UsageCount == 0;
}

public sealed record ReferenceListPage(
    string Key,
    IReadOnlyList<ReferenceListRow> Rows,
    ReferenceListTiles Tiles,
    int FilteredCount,
    int Page,
    int PageSize,
    int TotalPages,
    string Search,
    string Status)
{
    public static ReferenceListPage Empty(string key) =>
        new(key, Array.Empty<ReferenceListRow>(), new ReferenceListTiles(0, 0, 0), 0, 1, 25, 1, string.Empty, ReferenceListStatuses.All);
}

public interface IReferenceListService
{
    Task<ReferenceListPage> GetPageAsync(string key, ReferenceListRequest request, CancellationToken cancellationToken = default);

    Task<ReferenceListTiles> GetTilesAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>What uses one item, per kind of record.</summary>
    Task<IReadOnlyList<ReferenceListUsage>> GetUsageAsync(string key, int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an item nothing uses. An item in use is refused with "EntityInUse"; the
    /// database's foreign keys stay as the backstop for a record the check does not know.
    /// </summary>
    Task<AdminOperationResult> DeleteAsync(string key, int id, byte[] rowVersion, CancellationToken cancellationToken = default);
}

/// <summary>
/// The one rule for the reference lists on the shared list page (docs/ux/admin-review-3-fixes.md,
/// owner decision 1): tiles, the Used by column and Delete all count use the same way.
/// </summary>
public sealed class ReferenceListService : IReferenceListService
{
    private const int MaximumPageSize = 100;

    private readonly ApplicationDbContext _db;
    private readonly IAdminAuditService _audit;
    private readonly IAuthorizationService _authorization;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReferenceListService(
        ApplicationDbContext db,
        IAdminAuditService audit,
        IAuthorizationService authorization,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public async Task<ReferenceListPage> GetPageAsync(string key, ReferenceListRequest request, CancellationToken cancellationToken = default)
    {
        var definition = Require(key);
        var tiles = await CountTilesAsync(definition, cancellationToken);
        var search = request.Search?.Trim() ?? string.Empty;
        var status = ReferenceListStatuses.Normalize(request.Status);
        var pageSize = request.PageSize switch
        {
            < 10 => 25,
            > MaximumPageSize => MaximumPageSize,
            _ => request.PageSize
        };

        var all = definition.Items(_db);
        var filtered = status switch
        {
            ReferenceListStatuses.InUse => await WhereInUseAsync(definition, all, cancellationToken),
            ReferenceListStatuses.Active => all.Where(item => item.IsActive),
            ReferenceListStatuses.Inactive => all.Where(item => !item.IsActive),
            _ => all
        };

        if (search.Length > 0)
        {
            if (_db.Database.IsNpgsql())
            {
                var pattern = SearchLikePattern.Contains(search);
                filtered = filtered.Where(item => EF.Functions.ILike(item.Name, pattern, SearchLikePattern.EscapeCharacter));
            }
            else
            {
                var upper = search.ToUpperInvariant();
                filtered = filtered.Where(item => item.Name.ToUpper().Contains(upper));
            }
        }

        var filteredCount = await filtered.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)pageSize));
        var page = Math.Clamp(request.Page, 1, totalPages);

        var items = await filtered
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        // Moving is over the whole list, whatever the filter shows.
        var orderedIds = await all.OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        var positions = orderedIds.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);

        var ids = items.Select(item => item.Id).ToArray();
        var usage = await CountUsageAsync(definition, ids, cancellationToken);
        var extra = definition.LoadExtraColumns is null || ids.Length == 0
            ? null
            : await definition.LoadExtraColumns(_db, ids, cancellationToken);

        var rows = items.Select(item =>
        {
            var position = positions.TryGetValue(item.Id, out var found) ? found : -1;
            return new ReferenceListRow(
                item.Id,
                item.Name,
                item.SortOrder,
                item.IsActive,
                Convert.ToBase64String(item.RowVersion ?? Array.Empty<byte>()),
                usage[item.Id],
                position > 0,
                position >= 0 && position < orderedIds.Count - 1,
                ExtraValuesFor(definition, extra, item.Id));
        }).ToArray();

        return new ReferenceListPage(definition.Key, rows, tiles, filteredCount, page, pageSize, totalPages, search, status);
    }

    public Task<ReferenceListTiles> GetTilesAsync(string key, CancellationToken cancellationToken = default) =>
        CountTilesAsync(Require(key), cancellationToken);

    public async Task<IReadOnlyList<ReferenceListUsage>> GetUsageAsync(string key, int id, CancellationToken cancellationToken = default)
    {
        var usage = await CountUsageAsync(Require(key), new[] { id }, cancellationToken);
        return usage[id];
    }

    public async Task<AdminOperationResult> DeleteAsync(string key, int id, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        var definition = ReferenceListCatalog.Find(key);
        if (definition is null)
        {
            return AdminOperationResult.Failure("That list was not found.", "NotFound");
        }

        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal is null || !(await _authorization.AuthorizeAsync(principal, null, definition.Policy)).Succeeded)
        {
            return AdminOperationResult.Failure($"You are not allowed to change {definition.PluralLabel.ToLowerInvariant()}.", "Forbidden", _httpContextAccessor.HttpContext?.TraceIdentifier);
        }

        if (rowVersion is not { Length: > 0 })
        {
            return AdminOperationResult.Failure("The record version is missing. Reload the page and try again.", "ConcurrencyTokenMissing");
        }

        var entity = await definition.FindAsync(_db, id, cancellationToken);
        if (entity is null)
        {
            return AdminOperationResult.Failure($"That {definition.Label} is no longer in the list.", "NotFound");
        }

        var entry = _db.Entry(entity);
        var name = (string)entry.Property("Name").CurrentValue!;
        var sortOrder = (int)entry.Property("SortOrder").CurrentValue!;

        var usage = (await CountUsageAsync(definition, new[] { id }, cancellationToken))[id];
        var inUse = usage.Where(item => item.Count > 0).ToArray();
        if (inUse.Length > 0)
        {
            _db.ChangeTracker.Clear();
            return AdminOperationResult.Failure(
                $"'{name}' is used by {string.Join(" and ", inUse.Select(item => item.Text))}, so it cannot be deleted. Deactivate it instead: it stays on those records and drops out of new forms.",
                "EntityInUse");
        }

        var before = new
        {
            Id = id,
            Name = name,
            IsActive = (bool)entry.Property("IsActive").CurrentValue!,
            SortOrder = sortOrder
        };
        entry.Property("RowVersion").OriginalValue = rowVersion;
        definition.Remove(_db, entity);

        // Close the gap the item leaves, so the order reads 1, 2, 3 without a renumber.
        foreach (var later in await definition.LoadOrderedAfterAsync(_db, sortOrder, cancellationToken))
        {
            var property = _db.Entry(later).Property("SortOrder");
            property.CurrentValue = (int)property.CurrentValue! - 1;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.RecordAsync(new AdminAuditEntry(
                definition.DeleteAuditAction,
                definition.EntityType,
                id.ToString(),
                before,
                null,
                Origin: definition.AuditOrigin), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AdminOperationResult.Success($"Deleted '{name}'.");
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            return AdminOperationResult.Failure("This record was changed by another administrator. Reload the page and try again.", "ConcurrencyConflict");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // A record the check above does not know about still points at the item. The
            // database refused the delete, so nothing changed.
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return AdminOperationResult.Failure(
                $"'{name}' is still used by other records, so it cannot be deleted. Deactivate it instead.",
                "EntityInUse");
        }
    }

    private static ReferenceListDefinition Require(string key) =>
        ReferenceListCatalog.Find(key) ?? throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown reference list.");

    // Items some record uses. Sources the database can query stay one subquery; sources read in
    // memory (ids inside JSON, for example) are passed in as a list.
    private async Task<IQueryable<ReferenceListItemProjection>> WhereInUseAsync(
        ReferenceListDefinition definition,
        IQueryable<ReferenceListItemProjection> items,
        CancellationToken cancellationToken)
    {
        IQueryable<int>? queried = null;
        var loaded = new HashSet<int>();
        foreach (var source in definition.UsedBy)
        {
            if (source.ReferencingIds is not null)
            {
                var ids = source.ReferencingIds(_db);
                queried = queried is null ? ids : queried.Union(ids);
            }
            else
            {
                loaded.UnionWith(await source.LoadReferencingIds!(_db, cancellationToken));
            }
        }

        var loadedIds = loaded.ToArray();
        if (queried is null)
        {
            return items.Where(item => loadedIds.Contains(item.Id));
        }

        return loadedIds.Length == 0
            ? items.Where(item => queried.Contains(item.Id))
            : items.Where(item => queried.Contains(item.Id) || loadedIds.Contains(item.Id));
    }

    private async Task<ReferenceListTiles> CountTilesAsync(ReferenceListDefinition definition, CancellationToken cancellationToken)
    {
        var all = definition.Items(_db);
        var total = await all.CountAsync(cancellationToken);
        var inUse = await (await WhereInUseAsync(definition, all, cancellationToken)).CountAsync(cancellationToken);
        var inactive = await all.CountAsync(item => !item.IsActive, cancellationToken);
        return new ReferenceListTiles(total, inUse, inactive);
    }

    private async Task<IReadOnlyDictionary<int, IReadOnlyList<ReferenceListUsage>>> CountUsageAsync(
        ReferenceListDefinition definition,
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken)
    {
        var idArray = ids.Distinct().ToArray();
        var counts = new List<IReadOnlyDictionary<int, int>>();
        foreach (var source in definition.UsedBy)
        {
            if (idArray.Length == 0)
            {
                counts.Add(new Dictionary<int, int>());
                continue;
            }

            if (source.ReferencingIds is not null)
            {
                counts.Add(await source.ReferencingIds(_db)
                    .Where(value => idArray.Contains(value))
                    .GroupBy(value => value)
                    .Select(group => new { group.Key, Count = group.Count() })
                    .ToDictionaryAsync(item => item.Key, item => item.Count, cancellationToken));
            }
            else
            {
                counts.Add((await source.LoadReferencingIds!(_db, cancellationToken))
                    .Where(value => idArray.Contains(value))
                    .GroupBy(value => value)
                    .ToDictionary(group => group.Key, group => group.Count()));
            }
        }

        return idArray.ToDictionary(
            id => id,
            id => (IReadOnlyList<ReferenceListUsage>)definition.UsedBy
                .Select((source, index) => new ReferenceListUsage(source.One, source.Many, counts[index].TryGetValue(id, out var count) ? count : 0))
                .ToArray());
    }

    private static IReadOnlyList<string?> ExtraValuesFor(
        ReferenceListDefinition definition,
        IReadOnlyDictionary<int, IReadOnlyList<string?>>? extra,
        int id)
    {
        if (definition.ExtraColumns.Count == 0)
        {
            return Array.Empty<string?>();
        }

        return extra is not null && extra.TryGetValue(id, out var values)
            ? values
            : new string?[definition.ExtraColumns.Count];
    }
}
