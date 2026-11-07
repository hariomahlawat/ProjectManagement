using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Services.DocRepo;

// SECTION: Contracts
/// <summary>The two lists people file repository documents under.</summary>
public enum DocRepoListKind
{
    DocumentType,
    Office
}

/// <summary>
/// One row of a list. <see cref="Version"/> is the row's version for the forms on the page
/// (PostgreSQL's built-in xmin); null where the database has none (the test databases).
/// </summary>
public sealed record DocRepoListItem(
    int Id,
    string Name,
    int SortOrder,
    bool IsActive,
    uint? Version,
    int DocumentCount,
    int TrashedDocumentCount)
{
    /// <summary>A value can be deleted only when no document uses it, in the library or the trash.</summary>
    public bool CanDelete => DocumentCount == 0 && TrashedDocumentCount == 0;
}

public sealed record DocRepoListResult(bool Succeeded, string Message, string? ErrorCode = null)
{
    public static DocRepoListResult Success(string message) => new(true, message);

    public static DocRepoListResult Failure(string message, string code) => new(false, message, code);
}

public interface IDocRepoListService
{
    Task<IReadOnlyList<DocRepoListItem>> ListAsync(DocRepoListKind kind, CancellationToken cancellationToken = default);

    Task<DocRepoListResult> CreateAsync(DocRepoListKind kind, ClaimsPrincipal user, string? name, int? sortOrder, CancellationToken cancellationToken = default);

    Task<DocRepoListResult> UpdateAsync(DocRepoListKind kind, ClaimsPrincipal user, int id, string? name, int sortOrder, uint? version, CancellationToken cancellationToken = default);

    /// <summary>Sets the value active or not. It takes the state wanted, never a flip, so a repeated or late submit cannot undo it.</summary>
    Task<DocRepoListResult> SetActiveAsync(DocRepoListKind kind, ClaimsPrincipal user, int id, bool? isActive, uint? version, CancellationToken cancellationToken = default);

    /// <summary>Deletes a value no document uses (the trash included); a value in use can only be deactivated.</summary>
    Task<DocRepoListResult> DeleteAsync(DocRepoListKind kind, ClaimsPrincipal user, int id, uint? version, CancellationToken cancellationToken = default);
}

// SECTION: Service
/// <summary>
/// Repository document types and offices: listing, adding, renaming, reordering, switching on
/// and off, and deleting unused values. Both pages call this one service (they used to hold
/// near-identical copies of the logic). Each change is checked against
/// <see cref="Policies.Documents.ManageCategories"/>, the row's version and, for delete, the
/// documents that use the value; it is saved with its audit entry in one SaveChanges.
/// </summary>
public sealed class DocRepoListService : IDocRepoListService
{
    public const int NameMaxLength = 120;

    /// <summary>Shadow property mapped to PostgreSQL's xmin system column (no schema change).</summary>
    internal const string VersionProperty = "xmin";

    private readonly ApplicationDbContext _db;
    private readonly IDocRepoAuditService _audit;
    private readonly IAuthorizationService _authorization;

    public DocRepoListService(ApplicationDbContext db, IDocRepoAuditService audit, IAuthorizationService authorization)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    public Task<IReadOnlyList<DocRepoListItem>> ListAsync(DocRepoListKind kind, CancellationToken cancellationToken = default) =>
        kind == DocRepoListKind.DocumentType
            ? ListAsync(_db.DocumentCategories, kind, cancellationToken)
            : ListAsync(_db.OfficeCategories, kind, cancellationToken);

    public Task<DocRepoListResult> CreateAsync(DocRepoListKind kind, ClaimsPrincipal user, string? name, int? sortOrder, CancellationToken cancellationToken = default) =>
        kind == DocRepoListKind.DocumentType
            ? CreateAsync(_db.DocumentCategories, kind, user, name, sortOrder, (n, s) => new DocumentCategory { Name = n, SortOrder = s }, cancellationToken)
            : CreateAsync(_db.OfficeCategories, kind, user, name, sortOrder, (n, s) => new OfficeCategory { Name = n, SortOrder = s }, cancellationToken);

    public Task<DocRepoListResult> UpdateAsync(DocRepoListKind kind, ClaimsPrincipal user, int id, string? name, int sortOrder, uint? version, CancellationToken cancellationToken = default) =>
        kind == DocRepoListKind.DocumentType
            ? UpdateAsync(_db.DocumentCategories, kind, user, id, name, sortOrder, version, cancellationToken)
            : UpdateAsync(_db.OfficeCategories, kind, user, id, name, sortOrder, version, cancellationToken);

    public Task<DocRepoListResult> SetActiveAsync(DocRepoListKind kind, ClaimsPrincipal user, int id, bool? isActive, uint? version, CancellationToken cancellationToken = default) =>
        kind == DocRepoListKind.DocumentType
            ? SetActiveAsync(_db.DocumentCategories, kind, user, id, isActive, version, cancellationToken)
            : SetActiveAsync(_db.OfficeCategories, kind, user, id, isActive, version, cancellationToken);

    public Task<DocRepoListResult> DeleteAsync(DocRepoListKind kind, ClaimsPrincipal user, int id, uint? version, CancellationToken cancellationToken = default) =>
        kind == DocRepoListKind.DocumentType
            ? DeleteAsync(_db.DocumentCategories, kind, user, id, version, cancellationToken)
            : DeleteAsync(_db.OfficeCategories, kind, user, id, version, cancellationToken);

    // SECTION: List
    private async Task<IReadOnlyList<DocRepoListItem>> ListAsync<T>(DbSet<T> set, DocRepoListKind kind, CancellationToken cancellationToken)
        where T : class, IDocRepoListEntry
    {
        var ordered = set.AsNoTracking().OrderBy(item => item.SortOrder).ThenBy(item => item.Name);
        var rows = HasVersion<T>()
            ? await ordered
                .Select(item => new { item.Id, item.Name, item.SortOrder, item.IsActive, Version = (uint?)EF.Property<uint>(item, VersionProperty) })
                .ToListAsync(cancellationToken)
            : await ordered
                .Select(item => new { item.Id, item.Name, item.SortOrder, item.IsActive, Version = (uint?)null })
                .ToListAsync(cancellationToken);

        var counts = await UsageCountsAsync(kind, cancellationToken);
        return rows
            .Select(row =>
            {
                counts.TryGetValue(row.Id, out var usage);
                return new DocRepoListItem(row.Id, row.Name, row.SortOrder, row.IsActive, row.Version, usage.Live, usage.Trashed);
            })
            .ToList();
    }

    private async Task<Dictionary<int, (int Live, int Trashed)>> UsageCountsAsync(DocRepoListKind kind, CancellationToken cancellationToken)
    {
        var documents = _db.Documents.AsNoTracking();
        var grouped = kind == DocRepoListKind.DocumentType
            ? documents.GroupBy(d => new { Id = d.DocumentCategoryId, d.IsDeleted })
            : documents.GroupBy(d => new { Id = d.OfficeCategoryId, d.IsDeleted });
        var rows = await grouped
            .Select(group => new { group.Key.Id, group.Key.IsDeleted, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<int, (int Live, int Trashed)>();
        foreach (var row in rows)
        {
            result.TryGetValue(row.Id, out var usage);
            result[row.Id] = row.IsDeleted ? (usage.Live, usage.Trashed + row.Count) : (usage.Live + row.Count, usage.Trashed);
        }

        return result;
    }

    // SECTION: Create
    private async Task<DocRepoListResult> CreateAsync<T>(
        DbSet<T> set,
        DocRepoListKind kind,
        ClaimsPrincipal user,
        string? name,
        int? sortOrder,
        Func<string, int, T> create,
        CancellationToken cancellationToken)
        where T : class, IDocRepoListEntry
    {
        if (await AuthorizeAsync(user) is { } denied) return denied;
        if (ValidateName(name) is { } invalid) return invalid;

        var normalized = MasterDataName.Normalize(name);
        if (await NameTakenAsync(set, normalized, excludeId: null, cancellationToken))
        {
            return Duplicate(kind);
        }

        // A new entry goes to the end unless a position is typed. The old default of 100 gave
        // every entry the same position, so the list had no real order.
        var position = sortOrder ?? (await set.MaxAsync(item => (int?)item.SortOrder, cancellationToken) ?? 0) + 1;
        var entity = create(normalized, position);
        set.Add(entity);
        _audit.Stage(null, ActorUserId(user), DocRepoAuditEventTypes.CategoryChanged,
            new { kind = AuditKind(kind), action = "Created", entity.Name, entity.SortOrder });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return Duplicate(kind);
        }

        return DocRepoListResult.Success($"Added “{entity.Name}”.");
    }

    // SECTION: Rename and reorder
    private async Task<DocRepoListResult> UpdateAsync<T>(
        DbSet<T> set,
        DocRepoListKind kind,
        ClaimsPrincipal user,
        int id,
        string? name,
        int sortOrder,
        uint? version,
        CancellationToken cancellationToken)
        where T : class, IDocRepoListEntry
    {
        if (await AuthorizeAsync(user) is { } denied) return denied;
        if (ValidateName(name) is { } invalid) return invalid;
        if (sortOrder is < -999 or > 9999)
        {
            return DocRepoListResult.Failure("Position must be between -999 and 9999.", "ValidationFailed");
        }

        var entity = await set.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null) return NotFound(kind);

        var normalized = MasterDataName.Normalize(name);
        if (entity.Name == normalized && entity.SortOrder == sortOrder)
        {
            return DocRepoListResult.Success("No changes made.");
        }

        if (CheckVersion(entity, version) is { } stale) return stale;

        if (await NameTakenAsync(set, normalized, id, cancellationToken))
        {
            return Duplicate(kind);
        }

        _audit.Stage(null, ActorUserId(user), DocRepoAuditEventTypes.CategoryChanged,
            new { kind = AuditKind(kind), action = "Updated", entity.Id, from = new { entity.Name, entity.SortOrder }, to = new { Name = normalized, SortOrder = sortOrder } });
        entity.Name = normalized;
        entity.SortOrder = sortOrder;

        return await SaveAsync(kind, $"Saved “{normalized}”.", cancellationToken);
    }

    // SECTION: Switch on or off
    private async Task<DocRepoListResult> SetActiveAsync<T>(
        DbSet<T> set,
        DocRepoListKind kind,
        ClaimsPrincipal user,
        int id,
        bool? isActive,
        uint? version,
        CancellationToken cancellationToken)
        where T : class, IDocRepoListEntry
    {
        if (await AuthorizeAsync(user) is { } denied) return denied;

        // A form from before this change posts no target state. Flipping on its behalf is what let
        // a double submit undo itself, so it is refused instead.
        if (isActive is not { } target)
        {
            return DocRepoListResult.Failure("This page was out of date. It has been reloaded; please try again.", "TargetStateMissing");
        }

        var entity = await set.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null) return NotFound(kind);

        // Already in the wanted state: a second click, or another administrator got there first.
        // Nothing to do, and nothing is reversed.
        if (entity.IsActive == target)
        {
            return DocRepoListResult.Success(target ? $"“{entity.Name}” is already active." : $"“{entity.Name}” is already deactivated.");
        }

        if (CheckVersion(entity, version) is { } stale) return stale;

        entity.IsActive = target;
        _audit.Stage(null, ActorUserId(user), DocRepoAuditEventTypes.CategoryChanged,
            new { kind = AuditKind(kind), action = target ? "Activated" : "Deactivated", entity.Id, entity.Name });

        return await SaveAsync(kind, target ? $"Activated “{entity.Name}”." : $"Deactivated “{entity.Name}”.", cancellationToken);
    }

    // SECTION: Delete
    private async Task<DocRepoListResult> DeleteAsync<T>(
        DbSet<T> set,
        DocRepoListKind kind,
        ClaimsPrincipal user,
        int id,
        uint? version,
        CancellationToken cancellationToken)
        where T : class, IDocRepoListEntry
    {
        if (await AuthorizeAsync(user) is { } denied) return denied;

        // The database deletes a type's or office's documents with it (a cascading foreign key),
        // so the check and the delete run in one transaction holding the row's lock. Filing or
        // moving a document under this value needs a share lock on the same row, so it waits and
        // then fails rather than being deleted along with the value.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        if (_db.Database.IsNpgsql())
        {
            var table = _db.Model.FindEntityType(typeof(T))!.GetTableName();
            await _db.Database.ExecuteSqlRawAsync($"SELECT 1 FROM \"{table}\" WHERE \"Id\" = {{0}} FOR UPDATE", new object[] { id }, cancellationToken);
        }

        var entity = await set.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null) return NotFound(kind);

        // Trashed documents count: they still point at the value and can be restored.
        var documents = _db.Documents.AsNoTracking();
        var inUse = kind == DocRepoListKind.DocumentType
            ? await documents.CountAsync(d => d.DocumentCategoryId == id, cancellationToken)
            : await documents.CountAsync(d => d.OfficeCategoryId == id, cancellationToken);
        if (inUse > 0)
        {
            return InUse(entity.Name, inUse);
        }

        if (CheckVersion(entity, version) is { } stale) return stale;

        set.Remove(entity);
        _audit.Stage(null, ActorUserId(user), DocRepoAuditEventTypes.CategoryChanged,
            new { kind = AuditKind(kind), action = "Deleted", entity.Id, entity.Name, entity.SortOrder });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            return Concurrency();
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            // A document was filed under it after the check above.
            _db.ChangeTracker.Clear();
            return InUse(entity.Name, null);
        }

        return DocRepoListResult.Success($"Deleted “{entity.Name}”.");
    }

    // SECTION: Helpers
    private async Task<DocRepoListResult?> AuthorizeAsync(ClaimsPrincipal user)
    {
        var allowed = user is not null && (await _authorization.AuthorizeAsync(user, null, Policies.Documents.ManageCategories)).Succeeded;
        return allowed ? null : DocRepoListResult.Failure("You are not allowed to change the repository lists.", "Forbidden");
    }

    private bool HasVersion<T>() where T : class =>
        _db.Model.FindEntityType(typeof(T))?.FindProperty(VersionProperty) is not null;

    /// <summary>
    /// Refuses a change made from an out-of-date page, and makes the save itself conditional on
    /// the same version, so a change that lands between this read and the save is refused too.
    /// </summary>
    private DocRepoListResult? CheckVersion<T>(T entity, uint? version) where T : class
    {
        if (!HasVersion<T>())
        {
            return null;
        }

        if (version is not { } expected)
        {
            return DocRepoListResult.Failure("This page was out of date. It has been reloaded; please try again.", "ConcurrencyTokenMissing");
        }

        var property = _db.Entry(entity).Property(VersionProperty);
        if (property.CurrentValue is uint current && current != expected)
        {
            return Concurrency();
        }

        property.OriginalValue = expected;
        return null;
    }

    private async Task<DocRepoListResult> SaveAsync(DocRepoListKind kind, string message, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return DocRepoListResult.Success(message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            return Concurrency();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return Duplicate(kind);
        }
    }

    private static Task<bool> NameTakenAsync<T>(DbSet<T> set, string name, int? excludeId, CancellationToken cancellationToken)
        where T : class, IDocRepoListEntry
    {
        var canonical = MasterDataName.Canonical(name);
        return set.AsNoTracking().AnyAsync(
            item => (!excludeId.HasValue || item.Id != excludeId.Value) && item.Name.ToUpper() == canonical,
            cancellationToken);
    }

    private static DocRepoListResult? ValidateName(string? name)
    {
        var normalized = MasterDataName.Normalize(name);
        if (normalized.Length == 0)
        {
            return DocRepoListResult.Failure("Name is required.", "ValidationFailed");
        }

        return normalized.Length > NameMaxLength
            ? DocRepoListResult.Failure($"Name must be {NameMaxLength} characters or fewer.", "ValidationFailed")
            : null;
    }

    private static string ActorUserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.Identity?.Name ?? "system";

    private static string AuditKind(DocRepoListKind kind) => kind == DocRepoListKind.DocumentType ? "document" : "office";

    private static string Noun(DocRepoListKind kind) => kind == DocRepoListKind.DocumentType ? "type" : "office";

    private static DocRepoListResult Duplicate(DocRepoListKind kind) =>
        DocRepoListResult.Failure($"Another {Noun(kind)} already uses this name.", "DuplicateName");

    private static DocRepoListResult NotFound(DocRepoListKind kind) =>
        DocRepoListResult.Failure($"That {Noun(kind)} no longer exists. The list has been reloaded.", "NotFound");

    private static DocRepoListResult InUse(string name, int? count) =>
        DocRepoListResult.Failure(
            count is { } n
                ? $"“{name}” is used by {n:N0} document{(n == 1 ? "" : "s")} (the trash included), so it cannot be deleted. Deactivate it instead."
                : $"“{name}” is now used by a document, so it cannot be deleted. Deactivate it instead.",
            "InUse");

    private static DocRepoListResult Concurrency() =>
        DocRepoListResult.Failure("Someone else changed this entry after you opened the page. The list has been reloaded; please try again.", "ConcurrencyConflict");

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };
}
