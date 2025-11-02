using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents;

[Authorize(Policy = "DocRepo.Upload")]
[RequestFormLimits(MultipartBodyLengthLimit = 52_428_800)]
[RequestSizeLimit(52_428_800)]
public class UploadModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IDocStorage _storage;

    public UploadModel(ApplicationDbContext db, IDocStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public IReadOnlyList<OfficeCategory> OfficeOptions { get; private set; } = Array.Empty<OfficeCategory>();
    public IReadOnlyList<DocumentCategory> DocumentCategoryOptions { get; private set; } = Array.Empty<DocumentCategory>();

    public class InputModel
    {
        [Required, MaxLength(256)]
        public string Subject { get; set; } = string.Empty;

        [MaxLength(256)]
        public string? ReceivedFrom { get; set; }

        [DataType(DataType.Date)]
        public DateOnly? DocumentDate { get; set; }

        [Display(Name = "Office category"), Required]
        public int OfficeCategoryId { get; set; }

        [Display(Name = "Document category"), Required]
        public int DocumentCategoryId { get; set; }

        [Display(Name = "Tags (max 5)")]
        public List<string> Tags { get; set; } = new();

        [Required]
        public IFormFile? Pdf { get; set; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await PopulateLookupsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await PopulateLookupsAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Input.Pdf is null || Input.Pdf.Length == 0)
        {
            ModelState.AddModelError(nameof(Input.Pdf), "PDF is required.");
            return Page();
        }

        if (Input.Pdf.Length > 52_428_800)
        {
            ModelState.AddModelError(nameof(Input.Pdf), "File exceeds 50 MB limit.");
            return Page();
        }

        var isPdfContentType = string.Equals(Input.Pdf.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
        var hasPdfExtension = Path.GetExtension(Input.Pdf.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdfContentType || !hasPdfExtension)
        {
            ModelState.AddModelError(nameof(Input.Pdf), "Only PDF uploads are allowed.");
            return Page();
        }

        var normalizedTags = Input.Tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct()
            .Take(5)
            .ToList();

        Input.Tags = normalizedTags;

        if (normalizedTags.Any(tag => tag.Length is < 1 or > 32 || !IsValidTag(tag)))
        {
            ModelState.AddModelError(nameof(Input.Tags), "Tags must be 1-32 characters and contain only letters, numbers, spaces, hyphens, or underscores.");
            return Page();
        }

        string sha256Hex;
        await using (var stream = Input.Pdf.OpenReadStream())
        {
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream, cancellationToken);
            sha256Hex = Convert.ToHexString(hash);
        }

        var duplicate = await _db.Documents.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Sha256 == sha256Hex, cancellationToken);
        if (duplicate is not null)
        {
            ModelState.AddModelError(nameof(Input.Pdf),
                $"Duplicate document detected (matches {duplicate.Subject}). Upload blocked.");
            return Page();
        }

        var utcNow = DateTime.UtcNow;
        string storagePath;
        await using (var stream = Input.Pdf.OpenReadStream())
        {
            storagePath = await _storage.SaveAsync(stream, Path.GetFileName(Input.Pdf.FileName), utcNow, cancellationToken);
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            Subject = Input.Subject.Trim(),
            ReceivedFrom = string.IsNullOrWhiteSpace(Input.ReceivedFrom) ? null : Input.ReceivedFrom.Trim(),
            DocumentDate = Input.DocumentDate,
            OfficeCategoryId = Input.OfficeCategoryId,
            DocumentCategoryId = Input.DocumentCategoryId,
            OriginalFileName = Path.GetFileName(Input.Pdf.FileName),
            FileSizeBytes = Input.Pdf.Length,
            Sha256 = sha256Hex,
            StoragePath = storagePath,
            MimeType = "application/pdf",
            CreatedByUserId = User.Identity?.Name ?? "system",
            CreatedAtUtc = utcNow
        };

        if (normalizedTags.Count > 0)
        {
            var existingTags = await _db.Tags
                .Where(t => normalizedTags.Contains(t.Name))
                .ToListAsync(cancellationToken);

            var newTags = normalizedTags
                .Except(existingTags.Select(t => t.Name))
                .Select(name => new Tag { Name = name })
                .ToList();

            if (newTags.Count > 0)
            {
                await _db.Tags.AddRangeAsync(newTags, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                existingTags.AddRange(newTags);
            }

            document.DocumentTags = existingTags
                .Where(t => normalizedTags.Contains(t.Name))
                .Select(t => new DocumentTag
                {
                    DocumentId = document.Id,
                    TagId = t.Id
                })
                .ToList();
        }

        await _db.Documents.AddAsync(document, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["ToastMessage"] = "Document uploaded.";
        return RedirectToPage("./Index");
    }

    private async Task PopulateLookupsAsync(CancellationToken cancellationToken)
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

    private static bool IsValidTag(string tag)
    {
        foreach (var ch in tag)
        {
            if (!(char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
