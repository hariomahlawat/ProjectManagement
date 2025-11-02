using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents;

[Authorize(Policy = "DocRepo.View")]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    [FromQuery(Name = "q")] public string? Q { get; set; }
    [FromQuery(Name = "tag")] public string? Tag { get; set; }
    [FromQuery(Name = "officeCategoryId")] public long? OfficeCategoryId { get; set; }
    [FromQuery(Name = "documentCategoryId")] public long? DocumentCategoryId { get; set; }
    [FromQuery(Name = "year")] public int? Year { get; set; }

    private const int DefaultPageSize = 30;
    [FromQuery] public int Page { get; set; } = 1;
    [FromQuery] public int PageSize { get; set; } = DefaultPageSize;

    public int TotalCount { get; private set; }
    public int PageCount => (int)Math.Ceiling((double)TotalCount / PageSize);

    public List<Document> Items { get; private set; } = new();

    public List<OfficeCategory> OfficeCategories { get; private set; } = new();
    public List<DocumentCategory> DocumentCategories { get; private set; } = new();

    public async Task OnGetAsync()
    {
        if (Page < 1) Page = 1;
        if (PageSize < 10 || PageSize > 200) PageSize = DefaultPageSize;

        var q = _db.Documents
            .AsNoTracking()
            .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
            .Include(d => d.OfficeCategory)
            .Include(d => d.DocumentCategory)
            .Where(d => d.IsActive);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var pattern = $"%{Q.Trim()}%";
            q = q.Where(d =>
                EF.Functions.ILike(d.Subject, pattern) ||
                (d.ReceivedFrom != null && EF.Functions.ILike(d.ReceivedFrom, pattern)) ||
                d.DocumentTags.Any(dt => EF.Functions.ILike(dt.Tag.Name, pattern)));
        }

        if (!string.IsNullOrWhiteSpace(Tag))
        {
            var tnorm = Tag.Trim();
            q = q.Where(d => d.DocumentTags.Any(dt => dt.Tag.Name == tnorm || dt.Tag.NormalizedName == tnorm.ToLower()));
        }

        if (OfficeCategoryId.HasValue)
        {
            q = q.Where(d => d.OfficeCategoryId == OfficeCategoryId.Value);
        }

        if (DocumentCategoryId.HasValue)
        {
            q = q.Where(d => d.DocumentCategoryId == DocumentCategoryId.Value);
        }

        if (Year.HasValue)
        {
            q = q.Where(d => d.DocumentDate.HasValue && d.DocumentDate.Value.Year == Year.Value);
        }

        TotalCount = await q.CountAsync();

        q = q.OrderByDescending(d => d.DocumentDate ?? d.CreatedAtUtc);

        Items = await q.Skip((Page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        OfficeCategories = await _db.OfficeCategories
            .AsNoTracking()
            .Where(o => o.IsActive)
            .OrderBy(o => o.SortOrder)
            .ToListAsync();

        DocumentCategories = await _db.DocumentCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
    }
}
