using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Services.DocRepo;

/// <summary>One document whose text could not be read, as Unreadable PDFs lists it.</summary>
public sealed record OcrFailureRow(
    Guid Id,
    string Subject,
    string? DocumentCategoryName,
    string? OfficeCategoryName,
    long FileSizeBytes,
    string? OcrFailureReason,
    DateTime CreatedAtUtc);

/// <summary>One page of Unreadable PDFs.</summary>
public sealed record OcrFailurePage(
    IReadOnlyList<OcrFailureRow> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    /// <summary>1-based position of the first row on this page, or 0 when there are none.</summary>
    public int FirstItem => TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;

    public int LastItem => Math.Min(TotalCount, Page * PageSize);
}

public interface IOcrFailureQueryService
{
    /// <summary>
    /// Documents in the library (not in the bin) whose text could not be read, newest first, one
    /// page at a time. A page past the end shows the last page.
    /// </summary>
    Task<OcrFailurePage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}

/// <summary>
/// The Unreadable PDFs list. It used to load every failed document in the page model with no
/// paging (docs/ux/admin-review-3-fixes.md, C8).
/// </summary>
public sealed class OcrFailureQueryService : IOcrFailureQueryService
{
    public const int DefaultPageSize = 25;
    public static readonly IReadOnlyList<int> PageSizes = new[] { 10, 25, 50, 100 };

    private readonly ApplicationDbContext _db;

    public OcrFailureQueryService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public static int NormalizePageSize(int pageSize) => PageSizes.Contains(pageSize) ? pageSize : DefaultPageSize;

    public async Task<OcrFailurePage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = NormalizePageSize(pageSize);
        var failed = _db.Documents
            .AsNoTracking()
            .Where(d => d.OcrStatus == DocOcrStatus.Failed && !d.IsDeleted);

        var total = await failed.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var rows = await failed
            .OrderByDescending(d => d.CreatedAtUtc)
            .ThenBy(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new OcrFailureRow(
                d.Id,
                d.Subject,
                d.DocumentCategory != null ? d.DocumentCategory.Name : null,
                d.OfficeCategory != null ? d.OfficeCategory.Name : null,
                d.FileSizeBytes,
                d.OcrFailureReason,
                d.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new OcrFailurePage(rows, total, page, pageSize);
    }
}
