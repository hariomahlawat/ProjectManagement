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
    /// <summary>How deep the item sits in a tree list, for the indent; 0 at the top and in flat lists.</summary>
    public int Depth { get; init; }

    /// <summary>The item's parents, "Defence / Electronics", for a child in a tree list.</summary>
    public string? ParentPath { get; init; }

    /// <summary>A short line under the name, such as a description.</summary>
    public string? Detail { get; init; }

    /// <summary>The order as people read it: a position from 1, or the number they typed.</summary>
    public int ShownOrder { get; init; }

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

    /// <summary>One item as the list shows it (its parents and use included), or null.</summary>
    Task<ReferenceListRow?> GetItemAsync(string key, int id, CancellationToken cancellationToken = default);

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

        if (definition.IsTree)
        {
            return await GetTreePageAsync(definition, tiles, search, status, request.Page, pageSize, cancellationToken);
        }

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
                filtered = filtered.Where(item =>
                    EF.Functions.ILike(item.Name, pattern, SearchLikePattern.EscapeCharacter)
                    || (item.Detail != null && EF.Functions.ILike(item.Detail, pattern, SearchLikePattern.EscapeCharacter)));
            }
            else
            {
                var upper = search.ToUpperInvariant();
                filtered = filtered.Where(item =>
                    item.Name.ToUpper().Contains(upper)
                    || (item.Detail != null && item.Detail.ToUpper().Contains(upper)));
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
                ExtraValuesFor(definition, extra, item.Id))
            {
                Detail = item.Detail,
                ShownOrder = ShownOrder(definition, item.SortOrder)
            };
        }).ToArray();

        return new ReferenceListPage(definition.Key, rows, tiles, filteredCount, page, pageSize, totalPages, search, status);
    }

    // A list kept as a tree (categories) is small, so it is read whole and laid out in memory:
    // parents before their children, siblings by their order. With no filter the rows are
    // indented; with a filter or search they are not, and each child names its parents.
    private async Task<ReferenceListPage> GetTreePageAsync(
        ReferenceListDefinition definition,
        ReferenceListTiles tiles,
        string search,
        string status,
        int requestedPage,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var items = await definition.Items(_db).ToListAsync(cancellationToken);
        HashSet<int>? inUse = null;
        if (status == ReferenceListStatuses.InUse)
        {
            inUse = (await (await WhereInUseAsync(definition, definition.Items(_db), cancellationToken))
                .Select(item => item.Id)
                .ToListAsync(cancellationToken)).ToHashSet();
        }

        var matching = ReferenceListTree.Build(items)
            .Where(node => status switch
            {
                ReferenceListStatuses.InUse => inUse!.Contains(node.Item.Id),
                ReferenceListStatuses.Active => node.Item.IsActive,
                ReferenceListStatuses.Inactive => !node.Item.IsActive,
                _ => true
            })
            .Where(node => search.Length == 0
                || node.Item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (node.ParentPath?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        var indent = status == ReferenceListStatuses.All && search.Length == 0;
        var totalPages = Math.Max(1, (int)Math.Ceiling(matching.Count / (double)pageSize));
        var page = Math.Clamp(requestedPage, 1, totalPages);
        var shown = matching.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var ids = shown.Select(node => node.Item.Id).ToArray();
        var usage = await CountUsageAsync(definition, ids, cancellationToken);
        var extra = definition.LoadExtraColumns is null || ids.Length == 0
            ? null
            : await definition.LoadExtraColumns(_db, ids, cancellationToken);

        var rows = shown.Select(node => new ReferenceListRow(
            node.Item.Id,
            node.Item.Name,
            node.Item.SortOrder,
            node.Item.IsActive,
            Convert.ToBase64String(node.Item.RowVersion ?? Array.Empty<byte>()),
            usage[node.Item.Id],
            node.CanMoveUp,
            node.CanMoveDown,
            ExtraValuesFor(definition, extra, node.Item.Id))
        {
            Depth = indent ? node.Depth : 0,
            ParentPath = node.ParentPath,
            Detail = node.Item.Detail,
            ShownOrder = ShownOrder(definition, node.Item.SortOrder)
        }).ToArray();

        return new ReferenceListPage(definition.Key, rows, tiles, matching.Count, page, pageSize, totalPages, search, status);
    }

    private static int ShownOrder(ReferenceListDefinition definition, int sortOrder) =>
        definition.OrderIsPosition ? sortOrder + 1 : sortOrder;

    public Task<ReferenceListTiles> GetTilesAsync(string key, CancellationToken cancellationToken = default) =>
        CountTilesAsync(Require(key), cancellationToken);

    public async Task<IReadOnlyList<ReferenceListUsage>> GetUsageAsync(string key, int id, CancellationToken cancellationToken = default)
    {
        var usage = await CountUsageAsync(Require(key), new[] { id }, cancellationToken);
        return usage[id];
    }

    public async Task<ReferenceListRow?> GetItemAsync(string key, int id, CancellationToken cancellationToken = default)
    {
        var definition = Require(key);
        ReferenceListItemProjection? item;
        string? parentPath = null;
        if (definition.IsTree)
        {
            var node = ReferenceListTree.Build(await definition.Items(_db).ToListAsync(cancellationToken))
                .FirstOrDefault(candidate => candidate.Item.Id == id);
            item = node?.Item;
            parentPath = node?.ParentPath;
        }
        else
        {
            item = await definition.Items(_db).SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        }

        if (item is null)
        {
            return null;
        }

        var usage = await CountUsageAsync(definition, new[] { id }, cancellationToken);
        return new ReferenceListRow(
            item.Id,
            item.Name,
            item.SortOrder,
            item.IsActive,
            Convert.ToBase64String(item.RowVersion ?? Array.Empty<byte>()),
            usage[id],
            false,
            false,
            Array.Empty<string?>())
        {
            ParentPath = parentPath,
            Detail = item.Detail,
            ShownOrder = ShownOrder(definition, item.SortOrder)
        };
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
            return AdminOperationResult.Failure($"You are not allowed to change {definition.PluralInSentence}.", "Forbidden", _httpContextAccessor.HttpContext?.TraceIdentifier);
        }

        if (rowVersion is not { Length: > 0 })
        {
            return AdminOperationResult.Failure("The record version is missing. Reload the page and try again.", "ConcurrencyTokenMissing");
        }

        // The check and the delete run in one transaction, with the item's row locked first. A
        // record saved at the same moment that points at the item (PostgreSQL takes a key-share
        // lock on the item while checking its foreign key) either commits first and is counted
        // below, or waits until the delete is over and is then refused. Without this an ARPP
        // entry saved between the check and the delete would silently lose its value, because
        // its link is emptied on delete rather than refused (Codex review on #3018).
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        if (_db.Database.IsNpgsql())
        {
            await definition.LockAsync(_db, id, cancellationToken);
        }

        var entity = await definition.FindAsync(_db, id, cancellationToken);
        var item = await definition.Items(_db).SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entity is null || item is null)
        {
            return AdminOperationResult.Failure($"That {definition.Label} is no longer in the list.", "NotFound");
        }

        var entry = _db.Entry(entity);
        var name = item.Name;
        var sortOrder = item.SortOrder;

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
            item.IsActive,
            SortOrder = sortOrder,
            item.ParentId
        };
        entry.Property("RowVersion").OriginalValue = rowVersion;
        definition.Remove(_db, entity);

        // Close the gap the item leaves, so the order reads 1, 2, 3 without a renumber. In a
        // tree only its siblings move; an order people typed themselves is left as it is.
        if (definition.HasOrder && definition.OrderIsPosition)
        {
            foreach (var later in await definition.LoadOrderedAfterAsync(_db, sortOrder, item.ParentId, cancellationToken))
            {
                var property = _db.Entry(later).Property("SortOrder");
                property.CurrentValue = (int)property.CurrentValue! - 1;
            }
        }

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
