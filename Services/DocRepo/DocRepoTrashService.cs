using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services.Search;

namespace ProjectManagement.Services.DocRepo;

// SECTION: Contracts
public sealed record DocRepoTrashQuery(string? Search = null, int Page = 1, int PageSize = DocRepoTrashService.DefaultPageSize);

public sealed record DocRepoTrashRow(
    Guid Id,
    string Subject,
    string Office,
    string Category,
    DateTime? DeletedAtUtc,
    string? DeletedByUserId,
    string? DeleteReason,
    long FileSizeBytes);

public sealed record DocRepoTrashPage(
    IReadOnlyList<DocRepoTrashRow> Items,
    int TrashCount,
    long TrashBytes,
    int MatchCount,
    int Page,
    int PageSize,
    IReadOnlyDictionary<string, string> UserNames)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(MatchCount / (double)PageSize));

    /// <summary>1-based position of the first row on this page, or 0 when there are none.</summary>
    public int FirstItem => MatchCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;

    public int LastItem => Math.Min(MatchCount, Page * PageSize);
}

public enum DocRepoRestoreOutcome
{
    Restored,
    NotFound,
    Forbidden
}

// SECTION: Trash list and restore
/// <summary>
/// The repository's deleted documents: the paged list on the Trash page and restoring one.
/// Both need <see cref="Policies.Documents.Purge"/>, the same policy as the page. Restoring
/// undoes everything an approved deletion changed (RepositoryDocumentDeleteApprovalService
/// clears IsActive as well as setting IsDeleted), so the document is readable and searchable
/// again. The Search V2 database trigger on "Documents" queues the reindex on save.
/// Purging stays in <see cref="DocRepoTrashPurgeService"/>.
/// </summary>
public sealed class DocRepoTrashService
{
    public const int DefaultPageSize = 25;
    public static readonly IReadOnlyList<int> PageSizes = new[] { 10, 25, 50, 100 };

    private readonly ApplicationDbContext _db;
    private readonly IDocRepoAuditService _audit;
    private readonly IAuthorizationService _authorization;
    private readonly IClock _clock;

    public DocRepoTrashService(
        ApplicationDbContext db,
        IDocRepoAuditService audit,
        IAuthorizationService authorization,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public static int NormalizePageSize(int pageSize) => PageSizes.Contains(pageSize) ? pageSize : DefaultPageSize;

    /// <exception cref="UnauthorizedAccessException">The user may not manage the trash.</exception>
    public async Task<DocRepoTrashPage> ListAsync(ClaimsPrincipal user, DocRepoTrashQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await CanManageAsync(user))
        {
            throw new UnauthorizedAccessException("Only repository administrators can see deleted documents.");
        }

        var trash = _db.Documents.AsNoTracking().Where(d => d.IsDeleted);
        var trashCount = await trash.CountAsync(cancellationToken);
        var trashBytes = await trash.SumAsync(d => (long?)d.FileSizeBytes, cancellationToken) ?? 0;

        var matches = trash;
        var term = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if (term is not null)
        {
            var pattern = SearchLikePattern.Contains(term);
            matches = matches.Where(d => EF.Functions.ILike(d.Subject, pattern, SearchLikePattern.EscapeCharacter));
        }

        var matchCount = term is null ? trashCount : await matches.CountAsync(cancellationToken);
        var pageSize = NormalizePageSize(query.PageSize);
        var totalPages = Math.Max(1, (int)Math.Ceiling(matchCount / (double)pageSize));
        var page = Math.Min(Math.Max(1, query.Page), totalPages);

        var items = await matches
            .OrderByDescending(d => d.DeletedAtUtc ?? DateTime.MinValue)
            .ThenBy(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DocRepoTrashRow(
                d.Id,
                d.Subject,
                d.OfficeCategory != null ? d.OfficeCategory.Name : "",
                d.DocumentCategory != null ? d.DocumentCategory.Name : "",
                d.DeletedAtUtc,
                d.DeletedByUserId,
                d.DeleteReason,
                d.FileSizeBytes))
            .ToListAsync(cancellationToken);

        var userIds = items.Select(item => item.DeletedByUserId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        var userNames = userIds.Count == 0
            ? new Dictionary<string, string>()
            : await _db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName })
                .ToDictionaryAsync(u => u.Id, u => u.Name ?? u.Id, cancellationToken);

        return new DocRepoTrashPage(items, trashCount, trashBytes, matchCount, page, pageSize, userNames);
    }

    /// <summary>
    /// Puts a trashed document back in the library: active, not deleted, with the delete details
    /// cleared, and an audit entry saved with the change.
    /// </summary>
    public async Task<DocRepoRestoreOutcome> RestoreAsync(ClaimsPrincipal user, Guid documentId, CancellationToken cancellationToken = default)
    {
        if (!await CanManageAsync(user))
        {
            return DocRepoRestoreOutcome.Forbidden;
        }

        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && d.IsDeleted, cancellationToken);
        if (document is null)
        {
            return DocRepoRestoreOutcome.NotFound;
        }

        var actorId = ActorId(user);
        var now = _clock.UtcNow.UtcDateTime;
        document.IsDeleted = false;
        document.IsActive = true;
        document.DeletedAtUtc = null;
        document.DeletedByUserId = null;
        document.DeleteReason = null;
        document.UpdatedAtUtc = now;
        document.UpdatedByUserId = actorId;

        _audit.Stage(document.Id, actorId, "RestoredFromTrash", new { document.Id });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Purged (by an administrator or the retention worker) while this restore ran.
            _db.ChangeTracker.Clear();
            return DocRepoRestoreOutcome.NotFound;
        }

        return DocRepoRestoreOutcome.Restored;
    }

    private async Task<bool> CanManageAsync(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return (await _authorization.AuthorizeAsync(user, Policies.Documents.Purge)).Succeeded;
    }

    private static string ActorId(ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.Identity?.Name ?? "system";
}
