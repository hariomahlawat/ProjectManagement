using System.Globalization;
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

/// <summary>
/// One row of a list. <see cref="Id"/> is the item's key as a form posts it: a number for most
/// lists, a Guid for the social media lists.
/// </summary>
public sealed record ReferenceListRow(
    string Id,
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

    /// <summary>What uses one item of a list keyed by Guid (the social media lists).</summary>
    Task<IReadOnlyList<ReferenceListUsage>> GetUsageAsync(string key, Guid id, CancellationToken cancellationToken = default);

    /// <summary>One item as the list shows it (its parents and use included), or null.</summary>
    Task<ReferenceListRow?> GetItemAsync(string key, int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an item nothing uses. An item in use is refused with "EntityInUse"; the
    /// database's foreign keys stay as the backstop for a record the check does not know.
    /// </summary>
    Task<AdminOperationResult> DeleteAsync(string key, int id, byte[] rowVersion, CancellationToken cancellationToken = default);

    /// <summary>Deletes an unused item of a list keyed by Guid (the social media lists).</summary>
    Task<AdminOperationResult> DeleteAsync(string key, Guid id, byte[] rowVersion, CancellationToken cancellationToken = default);
}

/// <summary>What a list's rule needs from the application, handed to <see cref="ReferenceListDefinition.CreateEngine"/>.</summary>
internal sealed record ReferenceListServices(
    ApplicationDbContext Db,
    IAdminAuditService Audit,
    IAuthorizationService Authorization,
    IHttpContextAccessor HttpContextAccessor,
    IServiceProvider? Services);

/// <summary>One list's rule, whatever its key.</summary>
internal interface IReferenceListEngine
{
    Task<ReferenceListPage> GetPageAsync(ReferenceListRequest request, CancellationToken cancellationToken);
    Task<ReferenceListTiles> GetTilesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ReferenceListUsage>> GetUsageAsync(string id, CancellationToken cancellationToken);
    Task<ReferenceListRow?> GetItemAsync(string id, CancellationToken cancellationToken);
    Task<AdminOperationResult> DeleteAsync(string id, byte[] rowVersion, CancellationToken cancellationToken);
}

/// <summary>
/// The one rule for the reference lists on the shared list page (docs/ux/admin-review-3-fixes.md,
/// owner decision 1): tiles, the Used by column and Delete all count use the same way.
/// </summary>
public sealed class ReferenceListService : IReferenceListService
{
    private readonly ReferenceListServices _services;

    public ReferenceListService(
        ApplicationDbContext db,
        IAdminAuditService audit,
        IAuthorizationService authorization,
        IHttpContextAccessor httpContextAccessor,
        IServiceProvider? services = null)
    {
        _services = new ReferenceListServices(
            db ?? throw new ArgumentNullException(nameof(db)),
            audit ?? throw new ArgumentNullException(nameof(audit)),
            authorization ?? throw new ArgumentNullException(nameof(authorization)),
            httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor)),
            services);
    }

    public Task<ReferenceListPage> GetPageAsync(string key, ReferenceListRequest request, CancellationToken cancellationToken = default) =>
        Engine(key).GetPageAsync(request, cancellationToken);

    public Task<ReferenceListTiles> GetTilesAsync(string key, CancellationToken cancellationToken = default) =>
        Engine(key).GetTilesAsync(cancellationToken);

    public Task<IReadOnlyList<ReferenceListUsage>> GetUsageAsync(string key, int id, CancellationToken cancellationToken = default) =>
        Engine(key).GetUsageAsync(id.ToString(CultureInfo.InvariantCulture), cancellationToken);

    public Task<IReadOnlyList<ReferenceListUsage>> GetUsageAsync(string key, Guid id, CancellationToken cancellationToken = default) =>
        Engine(key).GetUsageAsync(id.ToString(), cancellationToken);

    public Task<ReferenceListRow?> GetItemAsync(string key, int id, CancellationToken cancellationToken = default) =>
        Engine(key).GetItemAsync(id.ToString(CultureInfo.InvariantCulture), cancellationToken);

    public Task<AdminOperationResult> DeleteAsync(string key, int id, byte[] rowVersion, CancellationToken cancellationToken = default) =>
        DeleteCoreAsync(key, id.ToString(CultureInfo.InvariantCulture), rowVersion, cancellationToken);

    public Task<AdminOperationResult> DeleteAsync(string key, Guid id, byte[] rowVersion, CancellationToken cancellationToken = default) =>
        DeleteCoreAsync(key, id.ToString(), rowVersion, cancellationToken);

    private Task<AdminOperationResult> DeleteCoreAsync(string key, string id, byte[] rowVersion, CancellationToken cancellationToken)
    {
        var definition = ReferenceListCatalog.Find(key);
        return definition is null
            ? Task.FromResult(AdminOperationResult.Failure("That list was not found.", "NotFound"))
            : definition.CreateEngine(_services).DeleteAsync(id, rowVersion, cancellationToken);
    }

    private IReferenceListEngine Engine(string key) =>
        (ReferenceListCatalog.Find(key) ?? throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown reference list."))
            .CreateEngine(_services);
}

/// <summary>The rule for one list, typed by its key (a number or a Guid).</summary>
internal sealed class ReferenceListEngine<TKey> : IReferenceListEngine where TKey : struct
{
    private const int MaximumPageSize = 100;

    private readonly KeyedReferenceListDefinition<TKey> _definition;
    private readonly ReferenceListServices _services;
    private readonly ApplicationDbContext _db;

    public ReferenceListEngine(KeyedReferenceListDefinition<TKey> definition, ReferenceListServices services)
    {
        _definition = definition;
        _services = services;
        _db = services.Db;
    }

    // The application's services, for usage sources and hooks that read settings or another
    // module's audit (the repository lists).
    private IServiceProvider? Provider => _services.Services ?? _services.HttpContextAccessor.HttpContext?.RequestServices;

    public async Task<ReferenceListPage> GetPageAsync(ReferenceListRequest request, CancellationToken cancellationToken)
    {
        var definition = _definition;
        var tiles = await GetTilesAsync(cancellationToken);
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
            return await GetTreePageAsync(tiles, search, status, request.Page, pageSize, cancellationToken);
        }

        var all = definition.Items(_db);
        var filtered = status switch
        {
            ReferenceListStatuses.InUse => await WhereInUseAsync(all, cancellationToken),
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
        var usage = await CountUsageAsync(ids, cancellationToken);
        var extra = definition.LoadExtraColumns is null || ids.Length == 0
            ? null
            : await definition.LoadExtraColumns(_db, ids, cancellationToken);

        var rows = items.Select(item =>
        {
            var position = positions.TryGetValue(item.Id, out var found) ? found : -1;
            return new ReferenceListRow(
                KeyedReferenceListDefinition<TKey>.Format(item.Id),
                item.Name,
                item.SortOrder,
                item.IsActive,
                item.RowVersionText,
                usage[item.Id],
                position > 0,
                position >= 0 && position < orderedIds.Count - 1,
                ExtraValuesFor(extra, item.Id))
            {
                Detail = item.Detail,
                ShownOrder = ShownOrder(item.SortOrder)
            };
        }).ToArray();

        return new ReferenceListPage(definition.Key, rows, tiles, filteredCount, page, pageSize, totalPages, search, status);
    }

    // A list kept as a tree (categories) is small, so it is read whole and laid out in memory:
    // parents before their children, siblings by their order. With no filter the rows are
    // indented; with a filter or search they are not, and each child names its parents.
    private async Task<ReferenceListPage> GetTreePageAsync(
        ReferenceListTiles tiles,
        string search,
        string status,
        int requestedPage,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var definition = _definition;
        var items = await definition.Items(_db).ToListAsync(cancellationToken);
        HashSet<TKey>? inUse = null;
        if (status == ReferenceListStatuses.InUse)
        {
            inUse = (await (await WhereInUseAsync(definition.Items(_db), cancellationToken))
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
        var usage = await CountUsageAsync(ids, cancellationToken);
        var extra = definition.LoadExtraColumns is null || ids.Length == 0
            ? null
            : await definition.LoadExtraColumns(_db, ids, cancellationToken);

        var rows = shown.Select(node => new ReferenceListRow(
            KeyedReferenceListDefinition<TKey>.Format(node.Item.Id),
            node.Item.Name,
            node.Item.SortOrder,
            node.Item.IsActive,
            node.Item.RowVersionText,
            usage[node.Item.Id],
            node.CanMoveUp,
            node.CanMoveDown,
            ExtraValuesFor(extra, node.Item.Id))
        {
            Depth = indent ? node.Depth : 0,
            ParentPath = node.ParentPath,
            Detail = node.Item.Detail,
            ShownOrder = ShownOrder(node.Item.SortOrder)
        }).ToArray();

        return new ReferenceListPage(definition.Key, rows, tiles, matching.Count, page, pageSize, totalPages, search, status);
    }

    private int ShownOrder(int sortOrder) =>
        _definition.OrderIsPosition ? sortOrder + 1 : sortOrder;

    public async Task<ReferenceListTiles> GetTilesAsync(CancellationToken cancellationToken)
    {
        var all = _definition.Items(_db);
        var total = await all.CountAsync(cancellationToken);
        var inUse = await (await WhereInUseAsync(all, cancellationToken)).CountAsync(cancellationToken);
        var inactive = await all.CountAsync(item => !item.IsActive, cancellationToken);
        return new ReferenceListTiles(total, inUse, inactive);
    }

    public async Task<IReadOnlyList<ReferenceListUsage>> GetUsageAsync(string id, CancellationToken cancellationToken)
    {
        if (!KeyedReferenceListDefinition<TKey>.TryParse(id, out var key))
        {
            return _definition.UsedBy.Select(source => new ReferenceListUsage(source.One, source.Many, 0)).ToArray();
        }

        var usage = await CountUsageAsync(new[] { key }, cancellationToken);
        return usage[key];
    }

    public async Task<ReferenceListRow?> GetItemAsync(string id, CancellationToken cancellationToken)
    {
        if (!KeyedReferenceListDefinition<TKey>.TryParse(id, out var key))
        {
            return null;
        }

        var definition = _definition;
        ReferenceListItemProjection<TKey>? item;
        string? parentPath = null;
        if (definition.IsTree)
        {
            var node = ReferenceListTree.Build(await definition.Items(_db).ToListAsync(cancellationToken))
                .FirstOrDefault(candidate => EqualityComparer<TKey>.Default.Equals(candidate.Item.Id, key));
            item = node?.Item;
            parentPath = node?.ParentPath;
        }
        else
        {
            item = await definition.Items(_db).SingleOrDefaultAsync(KeyedReferenceListDefinition<TKey>.IdIs(key), cancellationToken);
        }

        if (item is null)
        {
            return null;
        }

        var usage = await CountUsageAsync(new[] { key }, cancellationToken);
        return new ReferenceListRow(
            KeyedReferenceListDefinition<TKey>.Format(item.Id),
            item.Name,
            item.SortOrder,
            item.IsActive,
            item.RowVersionText,
            usage[key],
            false,
            false,
            Array.Empty<string?>())
        {
            ParentPath = parentPath,
            Detail = item.Detail,
            ShownOrder = ShownOrder(item.SortOrder)
        };
    }

    public async Task<AdminOperationResult> DeleteAsync(string id, byte[] rowVersion, CancellationToken cancellationToken)
    {
        var definition = _definition;
        var httpContext = _services.HttpContextAccessor.HttpContext;
        var principal = httpContext?.User;
        if (principal is null || !(await _services.Authorization.AuthorizeAsync(principal, null, definition.Policy)).Succeeded)
        {
            return AdminOperationResult.Failure($"You are not allowed to change {definition.PluralInSentence}.", "Forbidden", httpContext?.TraceIdentifier);
        }

        if (!KeyedReferenceListDefinition<TKey>.TryParse(id, out var key))
        {
            return AdminOperationResult.Failure($"That {definition.Label} is no longer in the list.", "NotFound");
        }

        if (definition.RequiresRowVersion(_db) && rowVersion is not { Length: > 0 })
        {
            return AdminOperationResult.Failure("The record version is missing. Reload the page and try again.", "ConcurrencyTokenMissing");
        }

        // The check and the delete run in one transaction, with the item's row locked first. A
        // record saved at the same moment that points at the item (PostgreSQL takes a key-share
        // lock on the item while checking its foreign key) either commits first and is counted
        // below, or waits until the delete is over and is then refused. Without this an ARPP
        // entry saved between the check and the delete would silently lose its value, because
        // its link is emptied on delete rather than refused (Codex review on #3018), and a
        // repository document filed at that moment would be deleted with its type or office,
        // because that link cascades.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        if (_db.Database.IsNpgsql())
        {
            await definition.LockAsync(_db, key, cancellationToken);
        }

        var entity = await definition.FindAsync(_db, key, cancellationToken);
        var item = await definition.Items(_db).SingleOrDefaultAsync(KeyedReferenceListDefinition<TKey>.IdIs(key), cancellationToken);
        if (entity is null || item is null)
        {
            return AdminOperationResult.Failure($"That {definition.Label} is no longer in the list.", "NotFound");
        }

        var entry = _db.Entry(entity);
        var name = item.Name;
        var sortOrder = item.SortOrder;

        var usage = (await CountUsageAsync(new[] { key }, cancellationToken))[key];
        var inUse = usage.Where(source => source.Count > 0).ToArray();
        if (inUse.Length > 0)
        {
            _db.ChangeTracker.Clear();
            return AdminOperationResult.Failure(
                $"'{name}' is used by {string.Join(" and ", inUse.Select(source => source.Text))}, so it cannot be deleted. Deactivate it instead: it stays on those records and drops out of new forms.",
                "EntityInUse");
        }

        if (!definition.ApplyRowVersion(entry, rowVersion ?? Array.Empty<byte>()))
        {
            _db.ChangeTracker.Clear();
            return Concurrency();
        }

        var before = new
        {
            Id = KeyedReferenceListDefinition<TKey>.Format(key),
            Name = name,
            item.IsActive,
            SortOrder = sortOrder,
            item.ParentId
        };
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

        await definition.OnDeletingAsync(
            new ReferenceListDeleteContext(_db, Provider, principal, KeyedReferenceListDefinition<TKey>.Format(key), name, sortOrder),
            cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            if (definition.WritesAdminAudit)
            {
                await _services.Audit.RecordAsync(new AdminAuditEntry(
                    definition.DeleteAuditAction,
                    definition.EntityType,
                    KeyedReferenceListDefinition<TKey>.Format(key),
                    before,
                    null,
                    Origin: definition.AuditOrigin), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return AdminOperationResult.Success($"Deleted '{name}'.");
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            return Concurrency();
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

    private static AdminOperationResult Concurrency() =>
        AdminOperationResult.Failure("This record was changed by another administrator. Reload the page and try again.", "ConcurrencyConflict");

    // Ids a source reads in memory, loaded once per engine: a page counts use for its tiles, its
    // filter and its rows, and must not read every change request three times (fourth review, C3).
    private readonly Dictionary<ReferenceListUsageSource<TKey>, IReadOnlyList<TKey>> _loaded = new();

    // The source as a database query, or null when it is read in memory on this database.
    private IQueryable<TKey>? Query(ReferenceListUsageSource<TKey> source) => source.ReferencingIds?.Invoke(_db);

    private async Task<IReadOnlyList<TKey>> LoadAsync(ReferenceListUsageSource<TKey> source, CancellationToken cancellationToken)
    {
        if (!_loaded.TryGetValue(source, out var ids))
        {
            ids = await source.LoadReferencingIds!(_db, Provider, cancellationToken);
            _loaded[source] = ids;
        }

        return ids;
    }

    // Items some record uses. Sources the database can query stay one subquery; sources read in
    // memory (ids inside JSON on the test databases, or in the settings) are passed in as a list.
    private async Task<IQueryable<ReferenceListItemProjection<TKey>>> WhereInUseAsync(
        IQueryable<ReferenceListItemProjection<TKey>> items,
        CancellationToken cancellationToken)
    {
        IQueryable<TKey>? queried = null;
        var loaded = new HashSet<TKey>();
        foreach (var source in _definition.UsedBy)
        {
            if (Query(source) is { } ids)
            {
                queried = queried is null ? ids : queried.Union(ids);
            }
            else
            {
                loaded.UnionWith(await LoadAsync(source, cancellationToken));
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

    private async Task<IReadOnlyDictionary<TKey, IReadOnlyList<ReferenceListUsage>>> CountUsageAsync(
        IReadOnlyCollection<TKey> ids,
        CancellationToken cancellationToken)
    {
        var idArray = ids.Distinct().ToArray();
        var counts = new List<IReadOnlyDictionary<TKey, int>>();
        foreach (var source in _definition.UsedBy)
        {
            if (idArray.Length == 0)
            {
                counts.Add(new Dictionary<TKey, int>());
                continue;
            }

            if (Query(source) is { } query)
            {
                counts.Add(await query
                    .Where(value => idArray.Contains(value))
                    .GroupBy(value => value)
                    .Select(group => new { group.Key, Count = group.Count() })
                    .ToDictionaryAsync(item => item.Key, item => item.Count, cancellationToken));
            }
            else
            {
                counts.Add((await LoadAsync(source, cancellationToken))
                    .Where(value => idArray.Contains(value))
                    .GroupBy(value => value)
                    .ToDictionary(group => group.Key, group => group.Count()));
            }
        }

        return idArray.ToDictionary(
            id => id,
            id => (IReadOnlyList<ReferenceListUsage>)_definition.UsedBy
                .Select((source, index) => new ReferenceListUsage(source.One, source.Many, counts[index].TryGetValue(id, out var count) ? count : 0))
                .ToArray());
    }

    private IReadOnlyList<string?> ExtraValuesFor(
        IReadOnlyDictionary<TKey, IReadOnlyList<string?>>? extra,
        TKey id)
    {
        if (_definition.ExtraColumns.Count == 0)
        {
            return Array.Empty<string?>();
        }

        return extra is not null && extra.TryGetValue(id, out var values)
            ? values
            : new string?[_definition.ExtraColumns.Count];
    }
}
