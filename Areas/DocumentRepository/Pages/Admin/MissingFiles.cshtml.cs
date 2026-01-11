using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin
{
    [Authorize(Policy = "DocRepo.DeleteApprove")]
    public sealed class MissingFilesModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IDocStorage _storage;

        // SECTION: Constructor
        public MissingFilesModel(ApplicationDbContext db, IDocStorage storage)
        {
            _db = db;
            _storage = storage;
        }

        // SECTION: View data
        public sealed record MissingFileRow(
            Guid Id,
            string Subject,
            string Office,
            string Category,
            DateOnly? DocumentDate,
            DateTime CreatedAtUtc,
            string StoragePath);

        public IReadOnlyList<MissingFileRow> Items { get; private set; } = Array.Empty<MissingFileRow>();

        public int PageNumber { get; private set; }

        public int PageSize { get; private set; }

        public int TotalCount { get; private set; }

        public int MissingCount { get; private set; }

        public bool IncludeDeleted { get; private set; }

        public int TotalPages => TotalCount == 0 || PageSize == 0
            ? 1
            : (int)Math.Ceiling(TotalCount / (double)PageSize);

        public bool HasPreviousPage => PageNumber > 1;

        public bool HasNextPage => PageNumber < TotalPages;

        // SECTION: Handlers
        public async Task OnGetAsync(int page = 1, int pageSize = 100, bool includeDeleted = false, CancellationToken cancellationToken = default)
        {
            // SECTION: Normalize inputs
            PageNumber = page < 1 ? 1 : page;
            PageSize = pageSize switch
            {
                < 25 => 25,
                > 200 => 200,
                _ => pageSize
            };
            IncludeDeleted = includeDeleted;

            var query = _db.Documents
                .AsNoTracking()
                .Where(d => includeDeleted || !d.IsDeleted)
                .OrderByDescending(d => d.CreatedAtUtc);

            TotalCount = await query.CountAsync(cancellationToken);

            // SECTION: Page selection
            var pageDocs = await query
                .Skip((PageNumber - 1) * PageSize)
                .Take(PageSize)
                .Select(d => new MissingFileRow(
                    d.Id,
                    d.Subject ?? string.Empty,
                    d.OfficeCategory != null ? d.OfficeCategory.Name : string.Empty,
                    d.DocumentCategory != null ? d.DocumentCategory.Name : string.Empty,
                    d.DocumentDate,
                    d.CreatedAtUtc,
                    d.StoragePath ?? string.Empty))
                .ToListAsync(cancellationToken);

            // SECTION: Storage checks (page only)
            var missingRows = new List<MissingFileRow>(pageDocs.Count);
            foreach (var row in pageDocs)
            {
                var exists = await _storage.ExistsAsync(row.StoragePath, cancellationToken);
                if (!exists)
                {
                    missingRows.Add(row);
                }
            }

            Items = missingRows;
            MissingCount = missingRows.Count;
        }
    }
}
