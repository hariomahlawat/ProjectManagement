using System.Buffers;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
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
            .ToList();

        if (normalizedTags.Count > 5)
        {
            ModelState.AddModelError(nameof(Input.Tags), "You can specify at most 5 tags.");
            return Page();
        }

        Input.Tags = normalizedTags;

        var invalidTag = normalizedTags.FirstOrDefault(tag => tag.Length is < 1 or > 32 || !IsValidTag(tag));
        if (invalidTag is not null)
        {
            ModelState.AddModelError(nameof(Input.Tags),
                $"Invalid tag '{invalidTag}'. Tags must be 1-32 characters and contain only letters, numbers, spaces, hyphens, or underscores.");
            return Page();
        }

        string sha256Hex;
        await using var uploadStream = Input.Pdf.OpenReadStream();
        await using var bufferedStream = new MemoryStream((int)Math.Min(Input.Pdf.Length, int.MaxValue));
        using var sha = SHA256.Create();

        var headerBuffer = new byte[5];
        var headerBytesRead = await uploadStream.ReadAsync(headerBuffer.AsMemory(0, headerBuffer.Length), cancellationToken);
        if (headerBytesRead < headerBuffer.Length || !HasPdfHeader(headerBuffer.AsSpan(0, headerBytesRead)))
        {
            ModelState.AddModelError(nameof(Input.Pdf), "Uploaded file is not a valid PDF.");
            return Page();
        }

        await bufferedStream.WriteAsync(headerBuffer.AsMemory(0, headerBytesRead), cancellationToken);
        sha.TransformBlock(headerBuffer, 0, headerBytesRead, null, 0);

        var rentedBuffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            int bytesRead;
            while ((bytesRead = await uploadStream.ReadAsync(rentedBuffer.AsMemory(0, rentedBuffer.Length), cancellationToken)) > 0)
            {
                await bufferedStream.WriteAsync(rentedBuffer.AsMemory(0, bytesRead), cancellationToken);
                sha.TransformBlock(rentedBuffer, 0, bytesRead, null, 0);
            }

            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }

        sha256Hex = Convert.ToHexString(sha.Hash!);
        if (bufferedStream.Length != Input.Pdf.Length)
        {
            ModelState.AddModelError(nameof(Input.Pdf), "Uploaded file could not be fully read. Please try again.");
            return Page();
        }

        var duplicate = await _db.Documents.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Sha256 == sha256Hex, cancellationToken);
        if (duplicate is not null)
        {
            ModelState.AddModelError(nameof(Input.Pdf),
                $"Duplicate document detected (matches {duplicate.Subject}). Upload blocked.");
            return Page();
        }

        bufferedStream.Position = 0;
        var utcNow = DateTime.UtcNow;
        var storagePath = await _storage.SaveAsync(bufferedStream, Path.GetFileName(Input.Pdf.FileName), utcNow, cancellationToken);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            Subject = Input.Subject.Trim(),
            ReceivedFrom = string.IsNullOrWhiteSpace(Input.ReceivedFrom) ? null : Input.ReceivedFrom.Trim(),
            DocumentDate = Input.DocumentDate,
            OfficeCategoryId = Input.OfficeCategoryId,
            DocumentCategoryId = Input.DocumentCategoryId,
            OriginalFileName = Path.GetFileName(Input.Pdf.FileName),
            FileSizeBytes = bufferedStream.Length,
            Sha256 = sha256Hex,
            StoragePath = storagePath,
            MimeType = "application/pdf",
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system",
            CreatedAtUtc = utcNow
        };

        if (normalizedTags.Count > 0)
        {
            var existingTags = await _db.Tags
                .Where(t => normalizedTags.Contains(t.NormalizedName))
                .ToListAsync(cancellationToken);

            var newTags = normalizedTags
                .Except(existingTags.Select(t => t.NormalizedName))
                .Select(name => new Tag { Name = name, NormalizedName = name })
                .ToList();

            if (newTags.Count > 0)
            {
                await _db.Tags.AddRangeAsync(newTags, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                existingTags.AddRange(newTags);
            }

            document.DocumentTags = existingTags
                .Where(t => normalizedTags.Contains(t.NormalizedName))
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

    private static bool HasPdfHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < 5)
        {
            return false;
        }

        return header[0] == (byte)'%' &&
               header[1] == (byte)'P' &&
               header[2] == (byte)'D' &&
               header[3] == (byte)'F' &&
               header[4] == (byte)'-';
    }
}
