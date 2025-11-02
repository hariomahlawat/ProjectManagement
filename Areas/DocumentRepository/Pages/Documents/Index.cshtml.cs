using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents;

[Authorize(Policy = "DocRepo.View")]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<DocumentRow> Documents { get; private set; } = Array.Empty<DocumentRow>();
    public IReadOnlyList<OfficeCategory> OfficeOptions { get; private set; } = Array.Empty<OfficeCategory>();
    public IReadOnlyList<DocumentCategory> DocumentCategoryOptions { get; private set; } = Array.Empty<DocumentCategory>();

    public string? Query { get; private set; }
    public string? Tag { get; private set; }
    public int? OfficeCategoryId { get; private set; }
    public int? DocumentCategoryId { get; private set; }
    public int? Year { get; private set; }

    public sealed record DocumentRow(Guid Id, string Subject, string? ReceivedFrom, DateOnly? DocumentDate, string Office, string Type);

    public async Task OnGetAsync(string? q, string? tag, int? officeCategoryId, int? documentCategoryId, int? year, CancellationToken cancellationToken)
    {
        Query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        Tag = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim().ToLowerInvariant();
        OfficeCategoryId = officeCategoryId;
        DocumentCategoryId = documentCategoryId;
        Year = year is > 0 ? year : null;

        await LoadLookupsAsync(cancellationToken);

        var queryable = _db.Documents.AsNoTracking()
            .Include(d => d.OfficeCategory)
            .Include(d => d.DocumentCategory)
            .Include(d => d.DocumentTags)
                .ThenInclude(dt => dt.Tag)
            .Where(d => d.IsActive);

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var search = $"%{Query.Trim()}%";
            queryable = queryable.Where(d =>
                EF.Functions.ILike(d.Subject, search) ||
                (d.ReceivedFrom != null && EF.Functions.ILike(d.ReceivedFrom, search)));
        }

        if (!string.IsNullOrWhiteSpace(Tag))
        {
            queryable = queryable.Where(d => d.DocumentTags.Any(t => t.Tag.Name == Tag));
        }

        if (OfficeCategoryId is { } officeId)
        {
            queryable = queryable.Where(d => d.OfficeCategoryId == officeId);
        }

        if (DocumentCategoryId is { } docCatId)
        {
            queryable = queryable.Where(d => d.DocumentCategoryId == docCatId);
        }

        if (Year is { } y)
        {
            queryable = queryable.Where(d => d.DocumentDate.HasValue && d.DocumentDate.Value.Year == y);
        }

        Documents = await queryable
            .OrderByDescending(d => d.DocumentDate)
            .ThenByDescending(d => d.CreatedAtUtc)
            .Select(d => new DocumentRow(
                d.Id,
                d.Subject,
                d.ReceivedFrom,
                d.DocumentDate,
                d.OfficeCategory.Name,
                d.DocumentCategory.Name))
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    private async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        OfficeOptions = await _db.OfficeCategories
            .Where(o => o.IsActive)
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.Name)
            .ToListAsync(cancellationToken);

        DocumentCategoryOptions = await _db.DocumentCategories
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }
}
