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
        private readonly IDocRepoMissingFilesService _missingFiles;

        // SECTION: Constructor
        public MissingFilesModel(ApplicationDbContext db, IDocRepoMissingFilesService missingFiles)
        {
            _db = db;
            _missingFiles = missingFiles;
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

        /// <summary>True when only the newest <see cref="CheckLimit"/> documents were checked.</summary>
        public bool Capped { get; private set; }

        public int CheckLimit { get; private set; }

        /// <summary>When the files were checked; the result is reused for a short while.</summary>
        public DateTimeOffset CheckedUtc { get; private set; }

        /// <summary>When the files were checked, in IST with the zone: "7 Nov 2026, 14:05 IST".</summary>
        public string CheckedAtIst => FormatCheckedAt(CheckedUtc);

        /// <summary>How long the shared check reuses its result, from the service's own setting.</summary>
        public string CacheMinutesText => FormatCacheDuration(DocRepoMissingFilesService.CacheDuration);

        internal static string FormatCheckedAt(DateTimeOffset utc) =>
            ProjectManagement.Infrastructure.IstClock.ToIst(utc).ToString("d MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " IST";

        internal static string FormatCacheDuration(TimeSpan duration) => duration.TotalSeconds < 60
            ? $"{(int)duration.TotalSeconds} seconds"
            : (int)duration.TotalMinutes == 1 ? "1 minute" : $"{(int)duration.TotalMinutes} minutes";

        public int TotalPages => MissingCount == 0 || PageSize == 0 ? 1 : (int)Math.Ceiling(MissingCount / (double)PageSize);

        public bool HasPreviousPage => PageNumber > 1;

        public bool HasNextPage => PageNumber < TotalPages;

        // SECTION: Handlers
        public async Task OnGetAsync(int page = 1, int pageSize = 100, bool includeDeleted = false, bool refresh = false, CancellationToken cancellationToken = default)
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

            // SECTION: The shared check (the same one System health reports), then this page of it
            var scan = await _missingFiles.ScanAsync(includeDeleted, refresh, cancellationToken);
            TotalCount = scan.CheckedCount;
            MissingCount = scan.MissingCount;
            Capped = scan.Capped;
            CheckLimit = scan.Limit;
            CheckedUtc = scan.CheckedUtc;
            if (PageNumber > TotalPages)
            {
                PageNumber = TotalPages;
            }

            var pageIds = scan.MissingIds.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
            if (pageIds.Count == 0)
            {
                return;
            }

            var rows = await _db.Documents
                .AsNoTracking()
                .Where(d => pageIds.Contains(d.Id))
                .Select(d => new MissingFileRow(
                    d.Id,
                    d.Subject ?? string.Empty,
                    d.OfficeCategory != null ? d.OfficeCategory.Name : string.Empty,
                    d.DocumentCategory != null ? d.DocumentCategory.Name : string.Empty,
                    d.DocumentDate,
                    d.CreatedAtUtc,
                    d.StoragePath ?? string.Empty))
                .ToListAsync(cancellationToken);

            // A document purged since the check is simply not listed.
            Items = rows.OrderBy(row => pageIds.IndexOf(row.Id)).ToList();
        }
    }
}
