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
    private readonly IDocStorage _storage;
    private readonly IDocRepoAuditService _audit;

    // SECTION: Constructor
    public IndexModel(ApplicationDbContext db, IDocStorage storage, IDocRepoAuditService audit)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
    }

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

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(document.Id, actorId, "RestoredFromTrash", new { document.Id }, cancellationToken);

        StatusMessage = "Document restored.";
        return RedirectToPage();
    }

    // SECTION: Purge handler
    public async Task<IActionResult> OnPostPurgeAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.IsDeleted, cancellationToken);
        if (document == null)
        {
            ErrorMessage = "Document not found or already purged.";
            return RedirectToPage();
        }

        var actorId = GetActorUserId();

        try
        {
            await _storage.DeleteAsync(document.StoragePath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            // File already removed – continue purging metadata.
        }
        catch (DirectoryNotFoundException)
        {
            // Directory missing – continue purging metadata.
        }
        catch (IOException ex)
        {
            ErrorMessage = $"Failed to delete file: {ex.Message}";
            return RedirectToPage();
        }

        _db.Documents.Remove(document);
        await _audit.WriteAsync(document.Id, actorId, "Purged", new { document.Id }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        StatusMessage = "Document permanently deleted.";
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
