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

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Items = await _db.Documents
            .AsNoTracking()
            .Include(d => d.OfficeCategory)
            .Include(d => d.DocumentCategory)
            .Where(d => d.IsDeleted)
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
