using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents
{
    [Authorize(Policy = "DocRepo.EditMetadata")]
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IAuthorizationService _authorizationService;
        private readonly IDocRepoAuditService _audit;

        public EditModel(ApplicationDbContext db, IAuthorizationService authorizationService, IDocRepoAuditService audit)
        {
            _db = db;
            _authorizationService = authorizationService;
            _audit = audit;
        }

        [BindProperty]
        public InputModel Input { get; set; } = null!;

        // Must match the bound field name so asp-validation-for shows the error.
        internal const string TagsKey = nameof(Input) + "." + nameof(InputModel.Tags);

        public List<OfficeCategory> OfficeCategories { get; private set; } = new();
        public List<DocumentCategory> DocumentCategories { get; private set; } = new();
        public bool CanMarkAots { get; private set; }

        public class InputModel
        {
            [Required]
            public Guid Id { get; set; }

            [Required, StringLength(256)]
            public string Subject { get; set; } = string.Empty;

            [Display(Name = "Received from"), StringLength(256)]
            public string? ReceivedFrom { get; set; }

            [Display(Name = "Document date")]
            public DateOnly? DocumentDate { get; set; }

            public int? OfficeCategoryId { get; set; }

            public int? DocumentCategoryId { get; set; }

            [Display(Name = "Tags (max 5, comma separated)"), MaxLength(128)]
            public string? Tags { get; set; }

            [Display(Name = "AOTS")]
            public bool IsAots { get; set; }

            /// <summary>Fingerprint of the values the form was opened with.</summary>
            public string? OriginalStamp { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
        {
            CanMarkAots = (await _authorizationService.AuthorizeAsync(User, "DocRepo.EditMetadata")).Succeeded;
            var doc = await _db.Documents
                .Include(d => d.DocumentTags)
                    .ThenInclude(dt => dt.Tag)
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

            if (doc is null || doc.IsDeleted)
                return NotFound();

            FillInput(doc);

            await LoadLookupsAsync(cancellationToken);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
        {
            CanMarkAots = (await _authorizationService.AuthorizeAsync(User, "DocRepo.EditMetadata")).Succeeded;
            if (!ModelState.IsValid)
            {
                await LoadLookupsAsync(cancellationToken);
                return Page();
            }

            // SECTION: Validate tags before touching the document
            var normalizedTags = ParseTags(Input.Tags).Distinct().ToList();
            if (normalizedTags.Count > 5)
            {
                ModelState.AddModelError(TagsKey, "You can specify at most 5 tags.");
                await LoadLookupsAsync(cancellationToken);
                return Page();
            }

            var badTag = normalizedTags.FirstOrDefault(t => t.Length is < 1 or > 32 || !IsValidTag(t));
            if (badTag is not null)
            {
                ModelState.AddModelError(TagsKey,
                    $"Invalid tag '{badTag}'. Tags must be 1–32 chars and can contain letters, numbers, spaces, hyphens, or underscores.");
                await LoadLookupsAsync(cancellationToken);
                return Page();
            }

            var doc = await _db.Documents
                .Include(d => d.DocumentTags)
                    .ThenInclude(dt => dt.Tag)
                .FirstOrDefaultAsync(d => d.Id == Input.Id, cancellationToken);

            if (doc is null || doc.IsDeleted)
            {
                return NotFound();
            }

            // SECTION: Concurrency check
            // The form carries a fingerprint of the values it was opened with. If someone else has
            // saved different values since, show their version instead of silently overwriting it.
            var before = Snapshot.Of(doc);
            if (!string.Equals(Input.OriginalStamp, before.Stamp, StringComparison.Ordinal))
            {
                FillInput(doc);
                ModelState.AddModelError(string.Empty,
                    "Someone else changed this document while you were editing. Their version is shown below; make your changes again and save.");
                await LoadLookupsAsync(cancellationToken);
                return Page();
            }

            // SECTION: Apply changes
            doc.Subject = Input.Subject.Trim();
            doc.ReceivedFrom = string.IsNullOrWhiteSpace(Input.ReceivedFrom) ? null : Input.ReceivedFrom.Trim();
            doc.DocumentDate = Input.DocumentDate;
            if (Input.OfficeCategoryId.HasValue)
                doc.OfficeCategoryId = Input.OfficeCategoryId.Value;
            if (Input.DocumentCategoryId.HasValue)
                doc.DocumentCategoryId = Input.DocumentCategoryId.Value;
            if (CanMarkAots)
                doc.IsAots = Input.IsAots;

            var currentNames = doc.DocumentTags.Select(dt => dt.Tag.NormalizedName).ToHashSet(StringComparer.Ordinal);
            if (!currentNames.SetEquals(normalizedTags))
            {
                var tags = await _db.Tags
                    .Where(t => normalizedTags.Contains(t.NormalizedName))
                    .ToListAsync(cancellationToken);
                foreach (var name in normalizedTags.Except(tags.Select(t => t.NormalizedName)))
                {
                    var tag = new Tag { Name = name, NormalizedName = name };
                    _db.Tags.Add(tag);
                    tags.Add(tag);
                }

                doc.DocumentTags.Clear();
                foreach (var tag in tags)
                {
                    doc.DocumentTags.Add(new DocumentTag { DocumentId = doc.Id, Tag = tag });
                }
            }

            var after = Snapshot.Of(doc, normalizedTags);
            var changes = before.Diff(after);
            if (changes.Count == 0)
            {
                TempData["ToastMessage"] = "No changes to save.";
                return RedirectToPage("./Index");
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";
            doc.UpdatedAtUtc = DateTime.UtcNow;
            doc.UpdatedByUserId = userId;

            // The change and its audit row are saved together.
            _audit.Stage(doc.Id, userId, DocRepoAuditEventTypes.MetadataEdited, new { doc.Id, changes });
            await _db.SaveChangesAsync(cancellationToken);

            TempData["ToastMessage"] = "Document metadata updated.";
            return RedirectToPage("./Index");
        }

        private void FillInput(Document doc)
        {
            Input = new InputModel
            {
                Id = doc.Id,
                Subject = doc.Subject,
                ReceivedFrom = doc.ReceivedFrom,
                DocumentDate = doc.DocumentDate,
                OfficeCategoryId = doc.OfficeCategoryId,
                DocumentCategoryId = doc.DocumentCategoryId,
                Tags = doc.DocumentTags.Any()
                    ? string.Join(", ", doc.DocumentTags.Select(dt => dt.Tag.Name).OrderBy(x => x))
                    : string.Empty,
                IsAots = doc.IsAots,
                OriginalStamp = Snapshot.Of(doc).Stamp
            };
            ModelState.Clear();
        }

        // SECTION: Editable-field snapshot (concurrency fingerprint and audit diff)
        internal sealed record Snapshot(
            string Subject,
            string? ReceivedFrom,
            DateOnly? DocumentDate,
            int OfficeCategoryId,
            int DocumentCategoryId,
            bool IsAots,
            string Tags)
        {
            public static Snapshot Of(Document doc, IEnumerable<string>? tagNames = null)
                => new(
                    doc.Subject,
                    doc.ReceivedFrom,
                    doc.DocumentDate,
                    doc.OfficeCategoryId,
                    doc.DocumentCategoryId,
                    doc.IsAots,
                    string.Join(",", (tagNames ?? doc.DocumentTags.Select(dt => dt.Tag.NormalizedName)).OrderBy(t => t, StringComparer.Ordinal)));

            public string Stamp
            {
                get
                {
                    var text = string.Join("\u001f", Subject, ReceivedFrom, DocumentDate?.ToString("O"), OfficeCategoryId, DocumentCategoryId, IsAots, Tags);
                    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
                }
            }

            public Dictionary<string, object?[]> Diff(Snapshot after)
            {
                var changes = new Dictionary<string, object?[]>();
                void Add<T>(string name, T from, T to)
                {
                    if (!EqualityComparer<T>.Default.Equals(from, to)) changes[name] = new object?[] { from, to };
                }

                Add(nameof(Subject), Subject, after.Subject);
                Add(nameof(ReceivedFrom), ReceivedFrom, after.ReceivedFrom);
                Add(nameof(DocumentDate), DocumentDate, after.DocumentDate);
                Add(nameof(OfficeCategoryId), OfficeCategoryId, after.OfficeCategoryId);
                Add(nameof(DocumentCategoryId), DocumentCategoryId, after.DocumentCategoryId);
                Add(nameof(IsAots), IsAots, after.IsAots);
                Add(nameof(Tags), Tags, after.Tags);
                return changes;
            }
        }

        private async Task LoadLookupsAsync(CancellationToken cancellationToken)
        {
            OfficeCategories = await _db.OfficeCategories
                .AsNoTracking()
                .Where(o => o.IsActive)
                .OrderBy(o => o.SortOrder)
                .ToListAsync(cancellationToken);

            DocumentCategories = await _db.DocumentCategories
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder)
                .ToListAsync(cancellationToken);
        }

        private static bool IsValidTag(string tag)
        {
            foreach (var ch in tag)
            {
                if (!(char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_'))
                    return false;
            }
            return true;
        }

        private static IEnumerable<string> ParseTags(string? tags)
        {
            if (string.IsNullOrWhiteSpace(tags))
                return Array.Empty<string>();

            return tags
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.ToLowerInvariant());
        }
    }
}
