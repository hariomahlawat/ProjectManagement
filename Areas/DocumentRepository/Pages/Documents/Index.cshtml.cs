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

        // filters
        [FromQuery(Name = "q")] public string? Q { get; set; }
        [FromQuery(Name = "tag")] public string? Tag { get; set; }
        [FromQuery(Name = "officeCategoryId")] public long? OfficeCategoryId { get; set; }
        [FromQuery(Name = "documentCategoryId")] public long? DocumentCategoryId { get; set; }
        [FromQuery(Name = "year")] public int? Year { get; set; }
        [FromQuery(Name = "includeInactive")] public bool IncludeInactive { get; set; }

        // paging
        private const int DefaultPageSize = 30;

        [FromQuery(Name = "page")]
        public int PageNumber { get; set; } = 1;

        [FromQuery]
        public int PageSize { get; set; } = DefaultPageSize;

        public int TotalCount { get; private set; }
        public int PageCount => (int)Math.Ceiling((double)TotalCount / PageSize);

        public List<Document> Items { get; private set; } = new();

        // for filter UI
        public List<OfficeCategory> OfficeCategories { get; private set; } = new();
        public List<DocumentCategory> DocumentCategories { get; private set; } = new();

        public async Task OnGetAsync()
        {
            // guard paging
            if (PageNumber < 1) PageNumber = 1;
            if (PageSize < 10 || PageSize > 200) PageSize = DefaultPageSize;

            // base query
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

            // text search
            if (!string.IsNullOrWhiteSpace(Q))
            {
                var text = Q.Trim();
                var pattern = $"%{text}%";

                q = q.Where(d =>
                    EF.Functions.ILike(d.Subject, pattern) ||
                    (d.ReceivedFrom != null && EF.Functions.ILike(d.ReceivedFrom, pattern)) ||
                    d.DocumentTags.Any(dt => EF.Functions.ILike(dt.Tag.Name, pattern)));
            }

            // exact tag
            if (!string.IsNullOrWhiteSpace(Tag))
            {
                var t = Tag.Trim();
                var tLower = t.ToLower();
                q = q.Where(d => d.DocumentTags.Any(dt =>
                    dt.Tag.Name == t || dt.Tag.NormalizedName == tLower));
            }

            if (OfficeCategoryId.HasValue)
            {
                var officeId = OfficeCategoryId.Value;
                q = q.Where(d => d.OfficeCategoryId == officeId);
            }

            if (DocumentCategoryId.HasValue)
            {
                var docCatId = DocumentCategoryId.Value;
                q = q.Where(d => d.DocumentCategoryId == docCatId);
            }

            // index-friendly year filter for DateOnly? column
            if (Year.HasValue)
            {
                var start = new DateOnly(Year.Value, 1, 1);
                var end = start.AddYears(1);

                q = q.Where(d =>
                    d.DocumentDate.HasValue &&
                    d.DocumentDate.Value >= start &&
                    d.DocumentDate.Value < end);
            }

            // count before paging
            TotalCount = await q.CountAsync();

            // ordering: first those with a date, then newest date, then created
            q = q
                .OrderByDescending(d => d.DocumentDate.HasValue)
                .ThenByDescending(d => d.DocumentDate)
                .ThenByDescending(d => d.CreatedAtUtc);

            // page
            Items = await q
                .Skip((PageNumber - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            // lookup data for filter modal
            OfficeCategories = await _db.OfficeCategories
                .AsNoTracking()
                .Where(o => o.IsActive)
                .OrderBy(o => o.SortOrder)
                .ThenBy(o => o.Name)
                .ToListAsync();

            DocumentCategories = await _db.DocumentCategories
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();
        }
    }
}
