using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents
{
    [Authorize(Policy = "DocRepo.EditMetadata")]
    public class AotsViewsModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        // SECTION: Constructor
        public AotsViewsModel(ApplicationDbContext db)
        {
            _db = db;
        }

        // SECTION: View data
        public string DocumentTitle { get; private set; } = "Document";

        public bool IsAots { get; private set; }

        public string ReturnUrl { get; private set; } = string.Empty;

        public IReadOnlyList<AotsViewerVm> Viewers { get; private set; } = Array.Empty<AotsViewerVm>();

        /// <summary>Active people who have not opened the document yet (signed-in human accounts only).</summary>
        public IReadOnlyList<AotsViewerVm> NotYetRead { get; private set; } = Array.Empty<AotsViewerVm>();

        public int ReadPercent => Viewers.Count + NotYetRead.Count == 0 ? 0 : (int)Math.Round(100.0 * Viewers.Count / (Viewers.Count + NotYetRead.Count));

        // SECTION: Query
        [FromQuery(Name = "returnUrl")]
        public string? ReturnUrlQuery { get; set; }

        // SECTION: Handlers
        public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
        {
            var document = await _db.Documents
                .AsNoTracking()
                .Where(doc => doc.Id == id)
                .Select(doc => new
                {
                    doc.Subject,
                    doc.IsAots,
                    doc.IsDeleted
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (document == null || document.IsDeleted)
            {
                return NotFound();
            }

            DocumentTitle = string.IsNullOrWhiteSpace(document.Subject)
                ? "Document"
                : document.Subject;

            IsAots = document.IsAots;

            // SECTION: Return URL
            var fallbackUrl = Url.Page("./Reader", new { id }) ?? "/DocumentRepository/Documents";
            ReturnUrl = Url.IsLocalUrl(ReturnUrlQuery) ? ReturnUrlQuery! : fallbackUrl;

            // SECTION: Viewer query
            Viewers = await (from view in _db.DocRepoAotsViews.AsNoTracking()
                             where view.DocumentId == id
                             join user in _db.Users.AsNoTracking()
                                 on view.UserId equals user.Id into userGroup
                             from user in userGroup.DefaultIfEmpty()
                             orderby view.FirstViewedAtUtc
                             select new AotsViewerVm
                             {
                                 Rank = user != null ? user.Rank : string.Empty,
                                 FullName = user != null && !string.IsNullOrWhiteSpace(user.FullName)
                                     ? user.FullName
                                     : "Unknown user",
                                 UserName = user != null && !string.IsNullOrWhiteSpace(user.UserName)
                                     ? user.UserName
                                     : view.UserId,
                                 FirstViewedAtUtc = view.FirstViewedAtUtc
                             })
                .ToListAsync(cancellationToken);

            var readerIds = _db.DocRepoAotsViews.Where(view => view.DocumentId == id).Select(view => view.UserId);
            NotYetRead = await _db.Users.AsNoTracking()
                .Where(user => user.AccountKind == UserAccountKind.Human && !user.IsDisabled && !user.PendingDeletion)
                .Where(user => !readerIds.Contains(user.Id))
                .OrderBy(user => user.FullName).ThenBy(user => user.UserName)
                .Select(user => new AotsViewerVm
                {
                    Rank = user.Rank,
                    FullName = string.IsNullOrWhiteSpace(user.FullName) ? (user.UserName ?? user.Id) : user.FullName,
                    UserName = user.UserName ?? user.Id
                })
                .ToListAsync(cancellationToken);

            return Page();
        }

        // SECTION: View models
        public sealed class AotsViewerVm
        {
            public string Rank { get; init; } = string.Empty;
            public string FullName { get; init; } = string.Empty;
            public string UserName { get; init; } = string.Empty;
            public DateTime FirstViewedAtUtc { get; init; }
        }
    }
}
