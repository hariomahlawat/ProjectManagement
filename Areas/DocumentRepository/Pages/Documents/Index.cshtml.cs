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

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents
{
    [Authorize(Policy = "DocRepo.View")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public IndexModel(ApplicationDbContext db) => _db = db;

        // Filters
        [FromQuery(Name = "q")] public string? Q { get; set; }
        [FromQuery(Name = "tag")] public string? Tag { get; set; }
        [FromQuery(Name = "officeCategoryId")] public long? OfficeCategoryId { get; set; }
        [FromQuery(Name = "documentCategoryId")] public long? DocumentCategoryId { get; set; }
        [FromQuery(Name = "year")] public int? Year { get; set; }
        [FromQuery(Name = "includeInactive")] public bool IncludeInactive { get; set; }

        // Paging (rename avoids CS0108 because PageModel already has Page())
        private const int DefaultPageSize = 30;

        [FromQuery(Name = "page")]
        public int PageNumber { get; set; } = 1;

        [FromQuery]
        public int PageSize { get; set; } = DefaultPageSize;

        public int TotalCount { get; private set; }
        public int PageCount => (int)Math.Ceiling((double)TotalCount / PageSize);

        public List<Document> Items { get; private set; } = new();

        // Reference lists for filters
        public List<OfficeCategory> OfficeCategories { get; private set; } = new();
        public List<DocumentCategory> DocumentCategories { get; private set; } = new();

        public async Task OnGetAsync()
        {
            // Guard page & page size
            if (PageNumber < 1) PageNumber = 1;
            if (PageSize < 10 || PageSize > 200) PageSize = DefaultPageSize;

            // Base query (EXPLICIT TYPE to avoid CS0266 on later reassignments)
            IQueryable<Document> q = _db.Documents
                .AsNoTracking()
                .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
                .Include(d => d.OfficeCategory)
                .Include(d => d.DocumentCategory)
                .Where(d => !d.IsDeleted);

            if (!IncludeInactive)
            {
                q = q.Where(d => d.IsActive);
            }

            // Text search: subject / received from / tag names
            if (!string.IsNullOrWhiteSpace(Q))
            {
                var pattern = $"%{Q.Trim()}%";
                q = q.Where(d =>
                    EF.Functions.ILike(d.Subject, pattern) ||
                    (d.ReceivedFrom != null && EF.Functions.ILike(d.ReceivedFrom, pattern)) ||
                    d.DocumentTags.Any(dt => EF.Functions.ILike(dt.Tag.Name, pattern)));
            }

            // Exact tag filter (by name/normalized)
            if (!string.IsNullOrWhiteSpace(Tag))
            {
                var tnorm = Tag.Trim();
                var tnormLower = tnorm.ToLower();
                q = q.Where(d => d.DocumentTags.Any(dt =>
                    dt.Tag.Name == tnorm || dt.Tag.NormalizedName == tnormLower));
            }

            if (OfficeCategoryId.HasValue)
                q = q.Where(d => d.OfficeCategoryId == OfficeCategoryId.Value);

            if (DocumentCategoryId.HasValue)
                q = q.Where(d => d.DocumentCategoryId == DocumentCategoryId.Value);

            if (Year.HasValue)
                q = q.Where(d => d.DocumentDate.HasValue && d.DocumentDate.Value.Year == Year.Value);

            // Count before paging
            TotalCount = await q.CountAsync();

            // Ordering without mixing DateOnly? and DateTime/DateTimeOffset
            q = q
                .OrderByDescending(d => d.DocumentDate.HasValue) // items with DocumentDate first
                .ThenByDescending(d => d.DocumentDate)           // newest DocumentDate first
                .ThenByDescending(d => d.CreatedAtUtc);          // fallback

            // Page
            Items = await q
                .Skip((PageNumber - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            // For filters UI
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
}
