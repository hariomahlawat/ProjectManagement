using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents;

[Authorize(Policy = "DocRepo.EditMetadata")]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public EditModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = null!;

    public List<OfficeCategory> OfficeCategories { get; private set; } = new();
    public List<DocumentCategory> DocumentCategories { get; private set; } = new();

    public class InputModel
    {
        [Required]
        public Guid Id { get; set; }

        [Required, StringLength(256)]
        public string Subject { get; set; } = string.Empty;

        [StringLength(256)]
        public string? ReceivedFrom { get; set; }

        public DateOnly? DocumentDate { get; set; }

        public int? OfficeCategoryId { get; set; }

        public int? DocumentCategoryId { get; set; }

        [Display(Name = "Tags (max 5, comma separated)"), MaxLength(128)]
        public string? Tags { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var doc = await _db.Documents
            .Include(d => d.DocumentTags)
                .ThenInclude(dt => dt.Tag)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (doc is null || doc.IsDeleted)
            return NotFound();

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
                : string.Empty
        };

        await LoadLookupsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync(cancellationToken);
            return Page();
        }

        var doc = await _db.Documents
            .Include(d => d.DocumentTags)
            .FirstOrDefaultAsync(d => d.Id == Input.Id, cancellationToken);

        if (doc is null || doc.IsDeleted)
            return NotFound();

        // basic fields
        doc.Subject = Input.Subject.Trim();
        doc.ReceivedFrom = string.IsNullOrWhiteSpace(Input.ReceivedFrom)
            ? null
            : Input.ReceivedFrom.Trim();
        doc.DocumentDate = Input.DocumentDate;

        if (Input.OfficeCategoryId.HasValue)
            doc.OfficeCategoryId = Input.OfficeCategoryId.Value;

        if (Input.DocumentCategoryId.HasValue)
            doc.DocumentCategoryId = Input.DocumentCategoryId.Value;

        // parse and validate tags
        var normalizedTags = ParseTags(Input.Tags).Distinct().ToList();

        if (normalizedTags.Count > 5)
        {
            ModelState.AddModelError(nameof(Input.Tags), "You can specify at most 5 tags.");
            await LoadLookupsAsync(cancellationToken);
            return Page();
        }

        var badTag = normalizedTags.FirstOrDefault(t => t.Length is < 1 or > 32 || !IsValidTag(t));
        if (badTag is not null)
        {
            ModelState.AddModelError(nameof(Input.Tags),
                $"Invalid tag '{badTag}'. Tags must be 1–32 chars and can contain letters, numbers, spaces, hyphens, or underscores.");
            await LoadLookupsAsync(cancellationToken);
            return Page();
        }

        // we will need tags from DB (existing + newly created)
        List<Tag> allTagsForDocument = new();

        if (normalizedTags.Count > 0)
        {
            // 1) fetch existing tags from DB
            var existingTags = await _db.Tags
                .Where(t => normalizedTags.Contains(t.NormalizedName))
                .ToListAsync(cancellationToken);

            // 2) find which ones are new
            var newTagNames = normalizedTags
                .Except(existingTags.Select(t => t.NormalizedName))
                .ToList();

            if (newTagNames.Count > 0)
            {
                var newTags = newTagNames
                    .Select(n => new Tag
                    {
                        Name = n,
                        NormalizedName = n
                    })
                    .ToList();

                await _db.Tags.AddRangeAsync(newTags, cancellationToken);
                // save once to get Tag.Id values
                await _db.SaveChangesAsync(cancellationToken);

                existingTags.AddRange(newTags);
            }

            // now we have all tag entities with real IDs
            allTagsForDocument = existingTags
                .Where(t => normalizedTags.Contains(t.NormalizedName))
                .ToList();
        }

        // rebuild document-tag links
        doc.DocumentTags.Clear();

        if (normalizedTags.Count > 0)
        {
            foreach (var tag in allTagsForDocument)
            {
                doc.DocumentTags.Add(new DocumentTag
                {
                    DocumentId = doc.Id,
                    TagId = tag.Id
                });
            }
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";
        doc.UpdatedAtUtc = DateTime.UtcNow;
        doc.UpdatedByUserId = userId;

        // final save (metadata + doc tags)
        await _db.SaveChangesAsync(cancellationToken);

        TempData["ToastMessage"] = "Document metadata updated.";
        return RedirectToPage("./Index");
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
