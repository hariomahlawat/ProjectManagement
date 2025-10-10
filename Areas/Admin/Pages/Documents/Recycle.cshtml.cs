using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Documents;

namespace ProjectManagement.Areas.Admin.Pages.Documents;

[Authorize(Roles = "Admin")]
public class RecycleModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IDocumentService _documents;
    private readonly IAuditService _audit;

    public RecycleModel(ApplicationDbContext db, IDocumentService documents, IAuditService audit)
    {
        _db = db;
        _documents = documents;
        _audit = audit;
    }

    [BindProperty(SupportsGet = true)]
    public int? ProjectId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? StageCode { get; set; }

    [BindProperty(SupportsGet = true)]
    [DataType(DataType.Date)]
    public DateTime? DeletedFrom { get; set; }

    [BindProperty(SupportsGet = true)]
    [DataType(DataType.Date)]
    public DateTime? DeletedTo { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? DeletedBy { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? SizeMinMb { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? SizeMaxMb { get; set; }

    [BindProperty]
    public List<int> SelectedIds { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public IReadOnlyList<SelectListItem> ProjectOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> StageOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<DocumentRow> Rows { get; private set; } = Array.Empty<DocumentRow>();

    public int TotalCount { get; private set; }

    public long TotalSizeBytes { get; private set; }

    public string TotalSizeDisplay => DocumentRow.FormatFileSize(TotalSizeBytes);

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostRestoreAsync(int id)
    {
        var actorId = GetUserId();
        try
        {
            var document = await _documents.RestoreAsync(id, actorId, HttpContext.RequestAborted);
            StatusMessage = $"Restored '{document.Title}' (#{document.Id}).";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(GetRouteValues());
    }

    public async Task<IActionResult> OnPostHardDeleteAsync(int id)
    {
        var actorId = GetUserId();
        var success = new List<int>();
        try
        {
            var existing = await _db.ProjectDocuments
                .Where(d => d.Id == id && d.Status == ProjectDocumentStatus.SoftDeleted)
                .Select(d => new { d.Id })
                .FirstOrDefaultAsync(HttpContext.RequestAborted);

            if (existing == null)
            {
                ErrorMessage = "Document not found or already removed.";
                return RedirectToPage(GetRouteValues());
            }

            await _documents.HardDeleteAsync(id, actorId, HttpContext.RequestAborted);
            success.Add(id);
            StatusMessage = "Permanently deleted 1 document.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        if (success.Count > 0)
        {
            await Audit.Events.ProjectDocumentsHardDeletedBulk(actorId, success)
                .WriteAsync(_audit);
        }

        return RedirectToPage(GetRouteValues());
    }

    public async Task<IActionResult> OnPostHardDeleteSelectedAsync()
    {
        if (SelectedIds == null || SelectedIds.Count == 0)
        {
            ErrorMessage = "Select at least one document.";
            return RedirectToPage(GetRouteValues());
        }

        var actorId = GetUserId();
        var requested = SelectedIds.Distinct().ToList();

        var existingSet = new HashSet<int>(await _db.ProjectDocuments.AsNoTracking()
            .Where(d => requested.Contains(d.Id) && d.Status == ProjectDocumentStatus.SoftDeleted)
            .Select(d => d.Id)
            .ToListAsync(HttpContext.RequestAborted));

        var successes = new List<int>();
        var failures = new List<string>();

        foreach (var docId in requested)
        {
            if (!existingSet.Contains(docId))
            {
                failures.Add($"#{docId} missing");
                continue;
            }

            try
            {
                await _documents.HardDeleteAsync(docId, actorId, HttpContext.RequestAborted);
                successes.Add(docId);
            }
            catch (Exception ex)
            {
                failures.Add($"#{docId} {ex.Message}");
            }
        }

        if (successes.Count > 0)
        {
            StatusMessage = successes.Count == 1
                ? "Permanently deleted 1 document."
                : $"Permanently deleted {successes.Count} documents.";

            await Audit.Events.ProjectDocumentsHardDeletedBulk(actorId, successes)
                .WriteAsync(_audit);
        }

        if (failures.Count > 0)
        {
            ErrorMessage = $"Some deletions failed: {string.Join(", ", failures)}";
        }

        return RedirectToPage(GetRouteValues());
    }

    public async Task<IActionResult> OnPostRestoreSelectedAsync()
    {
        if (SelectedIds == null || SelectedIds.Count == 0)
        {
            ErrorMessage = "Select at least one document.";
            return RedirectToPage(GetRouteValues());
        }

        var actorId = GetUserId();
        var requested = SelectedIds.Distinct().ToList();

        var existingDocs = await _db.ProjectDocuments.AsNoTracking()
            .Where(d => requested.Contains(d.Id) && d.Status == ProjectDocumentStatus.SoftDeleted)
            .Select(d => new { d.Id, d.ProjectId })
            .ToListAsync(HttpContext.RequestAborted);

        if (existingDocs.Count == 0)
        {
            ErrorMessage = "Selected documents were not found.";
            return RedirectToPage(GetRouteValues());
        }

        var successes = new List<int>();
        var failures = new List<string>();

        foreach (var doc in existingDocs)
        {
            try
            {
                await _documents.RestoreAsync(doc.Id, actorId, HttpContext.RequestAborted);
                successes.Add(doc.Id);
            }
            catch (Exception ex)
            {
                failures.Add($"#{doc.Id} {ex.Message}");
            }
        }

        if (successes.Count > 0)
        {
            StatusMessage = successes.Count == 1
                ? "Restored 1 document."
                : $"Restored {successes.Count} documents.";

            await Audit.Events.ProjectDocumentsRestoredBulk(actorId, successes)
                .WriteAsync(_audit);
        }

        if (failures.Count > 0)
        {
            ErrorMessage = $"Some restores failed: {string.Join(", ", failures)}";
        }

        return RedirectToPage(GetRouteValues());
    }

    private async Task LoadAsync()
    {
        ProjectOptions = await _db.Projects.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new SelectListItem(p.Name, p.Id.ToString(CultureInfo.InvariantCulture)))
            .ToListAsync();

        StageOptions = StageCodes.All
            .Select(code => new SelectListItem(StageCodes.DisplayNameOf(code), code))
            .ToList();

        var query = _db.ProjectDocuments
            .AsNoTracking()
            .Where(d => d.Status == ProjectDocumentStatus.SoftDeleted)
            .AsQueryable();

        if (ProjectId.HasValue)
        {
            query = query.Where(d => d.ProjectId == ProjectId.Value);
        }

        if (!string.IsNullOrWhiteSpace(StageCode))
        {
            query = query.Where(d => d.Stage != null && d.Stage.StageCode == StageCode);
        }

        if (DeletedFrom.HasValue)
        {
            var fromUtc = ConvertToUtc(DeletedFrom.Value);
            query = query.Where(d => d.ArchivedAtUtc >= fromUtc);
        }

        if (DeletedTo.HasValue)
        {
            var toUtc = ConvertToUtc(DeletedTo.Value.AddDays(1).AddTicks(-1));
            query = query.Where(d => d.ArchivedAtUtc <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(DeletedBy))
        {
            var term = DeletedBy.Trim();
            if (SupportsILike())
            {
                query = query.Where(d =>
                    (d.ArchivedByUser != null && EF.Functions.ILike(d.ArchivedByUser.FullName ?? string.Empty, $"%{term}%")) ||
                    (d.ArchivedByUser != null && EF.Functions.ILike(d.ArchivedByUser.UserName ?? string.Empty, $"%{term}%")) ||
                    (d.ArchivedByUserId != null && EF.Functions.ILike(d.ArchivedByUserId, $"%{term}%")));
            }
            else
            {
                var lowered = term.ToLowerInvariant();
                query = query.Where(d =>
                    (d.ArchivedByUser != null && d.ArchivedByUser.FullName != null && d.ArchivedByUser.FullName.ToLower().Contains(lowered)) ||
                    (d.ArchivedByUser != null && d.ArchivedByUser.UserName != null && d.ArchivedByUser.UserName.ToLower().Contains(lowered)) ||
                    (d.ArchivedByUserId != null && d.ArchivedByUserId.ToLower().Contains(lowered)));
            }
        }

        if (SizeMinMb.HasValue)
        {
            var minBytes = (long)Math.Max(SizeMinMb.Value, 0) * 1024 * 1024;
            query = query.Where(d => d.FileSize >= minBytes);
        }

        if (SizeMaxMb.HasValue)
        {
            var maxBytes = (long)Math.Max(SizeMaxMb.Value, 0) * 1024 * 1024;
            query = query.Where(d => d.FileSize <= maxBytes);
        }

        TotalCount = await query.CountAsync(HttpContext.RequestAborted);
        TotalSizeBytes = await query.SumAsync(d => (long?)d.FileSize, HttpContext.RequestAborted) ?? 0;

        Rows = await query
            .OrderByDescending(d => d.ArchivedAtUtc)
            .Select(d => new DocumentRow
            {
                DocumentId = d.Id,
                ProjectId = d.ProjectId,
                ProjectName = d.Project.Name,
                StageCode = d.Stage != null ? d.Stage.StageCode : null,
                Title = d.Title,
                OriginalFileName = d.OriginalFileName,
                FileSize = d.FileSize,
                DeletedAtUtc = d.ArchivedAtUtc,
                DeletedByUserId = d.ArchivedByUserId,
                DeletedByUserName = d.ArchivedByUser != null ? (d.ArchivedByUser.FullName ?? d.ArchivedByUser.UserName) : null
            })
            .ToListAsync(HttpContext.RequestAborted);
    }

    private static bool SupportsILike()
        => EF.Functions.GetType().GetMethod("ILike") != null;

    private static DateTimeOffset ConvertToUtc(DateTime date)
    {
        var unspecified = DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, IstClock.TimeZone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private object GetRouteValues() => new
    {
        ProjectId,
        StageCode,
        DeletedFrom = DeletedFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DeletedTo = DeletedTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DeletedBy,
        SizeMinMb,
        SizeMaxMb
    };

    private string GetUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    public sealed class DocumentRow
    {
        public int DocumentId { get; init; }
        public int ProjectId { get; init; }
        public string ProjectName { get; init; } = string.Empty;
        public string? StageCode { get; init; }
        public string Title { get; init; } = string.Empty;
        public string OriginalFileName { get; init; } = string.Empty;
        public long FileSize { get; init; }
        public DateTimeOffset? DeletedAtUtc { get; init; }
        public string? DeletedByUserId { get; init; }
        public string? DeletedByUserName { get; init; }

        public string StageDisplayName => StageCode is null ? "—" : StageCodes.DisplayNameOf(StageCode);

        public string DeletedByDisplay => !string.IsNullOrWhiteSpace(DeletedByUserName)
            ? DeletedByUserName!
            : (DeletedByUserId ?? "—");

        public string FileSizeDisplay => FormatFileSize(FileSize);

        public static string FormatFileSize(long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }

            double size = bytes / 1024d;
            string[] units = { "KB", "MB", "GB", "TB", "PB" };
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", size, units[unitIndex]);
        }
    }
}
