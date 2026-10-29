using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Search;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin.Trash;

[Authorize(Policy = "DocRepo.Purge")]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IDocRepoAuditService _audit;
    private readonly DocRepoTrashPurgeService _purge;

    // SECTION: Constructor
    public IndexModel(ApplicationDbContext db, IDocRepoAuditService audit, DocRepoTrashPurgeService purge)
    {
        _db = db;
        _audit = audit;
        _purge = purge;
    }

    /// <summary>Days before a trashed document is purged automatically; 0 when that is off.</summary>
    public int RetentionDays => _purge.RetentionDays;

    public DateTime? PurgeDueUtc(DateTime? deletedAtUtc) => _purge.PurgeDueUtc(deletedAtUtc);

    // SECTION: View data
    public IReadOnlyList<DeletedDocumentRow> Items { get; private set; } = Array.Empty<DeletedDocumentRow>();

    /// <summary>Everything in the trash, before the search narrows it.</summary>
    public int TotalCount { get; private set; }

    public long TotalBytes { get; private set; }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    public IReadOnlyDictionary<string, string> UserNames { get; private set; } = new Dictionary<string, string>();

    public string NameOf(string? userId)
        => string.IsNullOrWhiteSpace(userId) ? "Unknown" : UserNames.TryGetValue(userId, out var name) ? name : userId;

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var term = string.IsNullOrWhiteSpace(Query) ? null : Query.Trim();
        var pattern = term is null ? string.Empty : SearchLikePattern.Contains(term);
        TotalCount = await _db.Documents.CountAsync(d => d.IsDeleted, cancellationToken);
        TotalBytes = await _db.Documents.Where(d => d.IsDeleted).SumAsync(d => (long?)d.FileSizeBytes, cancellationToken) ?? 0;

        Items = await _db.Documents
            .AsNoTracking()
            .Include(d => d.OfficeCategory)
            .Include(d => d.DocumentCategory)
            .Where(d => d.IsDeleted)
            .Where(d => string.IsNullOrEmpty(term) || EF.Functions.ILike(d.Subject, pattern, SearchLikePattern.EscapeCharacter))
            .OrderByDescending(d => d.DeletedAtUtc ?? DateTime.MinValue)
            .Select(d => new DeletedDocumentRow(
                d.Id,
                d.Subject,
                d.OfficeCategory != null ? d.OfficeCategory.Name : "",
                d.DocumentCategory != null ? d.DocumentCategory.Name : "",
                d.DeletedAtUtc,
                d.DeletedByUserId,
                d.DeleteReason,
                d.StoragePath,
                d.FileSizeBytes))
            .ToListAsync(cancellationToken);

        var userIds = Items.Select(item => item.DeletedByUserId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        UserNames = await _db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, Name = string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName })
            .ToDictionaryAsync(user => user.Id, user => user.Name ?? user.Id, cancellationToken);
    }

    // SECTION: Restore handler
    public async Task<IActionResult> OnPostRestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.IsDeleted, cancellationToken);
        if (document == null)
        {
            ErrorMessage = "Document not found or already restored.";
            return RedirectToPage();
        }

        var actorId = GetActorUserId();
        document.IsDeleted = false;
        document.DeletedAtUtc = null;
        document.DeletedByUserId = null;
        document.DeleteReason = null;
        document.UpdatedAtUtc = DateTime.UtcNow;
        document.UpdatedByUserId = actorId;

        _audit.Stage(document.Id, actorId, "RestoredFromTrash", new { document.Id });
        await _db.SaveChangesAsync(cancellationToken);

        StatusMessage = "Document restored.";
        return RedirectToPage();
    }

    // SECTION: Purge handler
    public async Task<IActionResult> OnPostPurgeAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _purge.PurgeAsync(id, GetActorUserId(), cancellationToken: cancellationToken);
        switch (result.Outcome)
        {
            case DocRepoPurgeOutcome.NotFound:
                ErrorMessage = "Document not found or already purged.";
                break;
            case DocRepoPurgeOutcome.FileError:
                ErrorMessage = $"Failed to delete file: {result.Error}";
                break;
            default:
                StatusMessage = "Document permanently deleted.";
                break;
        }

        return RedirectToPage();
    }

    // SECTION: Helpers
    private string GetActorUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";

    // SECTION: View model
    public sealed record DeletedDocumentRow(
        Guid Id,
        string Subject,
        string Office,
        string Category,
        DateTime? DeletedAtUtc,
        string? DeletedByUserId,
        string? DeleteReason,
        string StoragePath,
        long FileSizeBytes);
}
