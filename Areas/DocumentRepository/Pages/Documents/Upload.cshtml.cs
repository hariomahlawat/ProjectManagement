using System.Buffers;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents;

[Authorize(Policy = "DocRepo.Upload")]
[EnableRateLimiting("docUpload")]
[RequestFormLimits(MultipartBodyLengthLimit = 52_428_800)]
[RequestSizeLimit(52_428_800)]
public class UploadModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IDocStorage _storage;
    private readonly IFileScanner _scanner;
    private readonly IDocRepoAuditService _audit;

    public UploadModel(ApplicationDbContext db, IDocStorage storage, IFileScanner scanner, IDocRepoAuditService audit)
    {
        _db = db;
        _storage = storage;
        _scanner = scanner;
        _audit = audit;
    }

    public IReadOnlyList<OfficeCategory> OfficeOptions { get; private set; } = Array.Empty<OfficeCategory>();
    public IReadOnlyList<DocumentCategory> DocumentCategoryOptions { get; private set; } = Array.Empty<DocumentCategory>();
    public SelectList OfficeSelectList { get; private set; } = default!;
    public SelectList DocumentTypeSelectList { get; private set; } = default!;
    public string? PdfPreviewUrl { get; private set; }
    public bool HasPreview => !string.IsNullOrEmpty(PdfPreviewUrl);

    public class InputModel
    {
        [Required, MaxLength(256)]
        public string Subject { get; set; } = string.Empty;

        [MaxLength(256)]
        public string? ReceivedFrom { get; set; }

        [DataType(DataType.Date)]
        public DateOnly? DocumentDate { get; set; }

        [Display(Name = "Office category"), Required]
        public int? OfficeCategoryId { get; set; }

        [Display(Name = "Document category"), Required]
        public int? DocumentCategoryId { get; set; }

        [Display(Name = "Tags (max 5, comma separated)"), MaxLength(128)]
        public string? Tags { get; set; }

        [Required]
        public IFormFile? File { get; set; }
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

        if (Input.File is null || Input.File.Length == 0)
        {
            ModelState.AddModelError(nameof(Input.File), "PDF is required.");
            return Page();
        }

        if (Input.File.Length > 52_428_800)
        {
            ModelState.AddModelError(nameof(Input.File), "File exceeds 50 MB limit.");
            return Page();
        }

        var isPdfContentType = string.Equals(Input.File.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
        var hasPdfExtension = Path.GetExtension(Input.File.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdfContentType || !hasPdfExtension)
        {
            ModelState.AddModelError(nameof(Input.File), "Only PDF uploads are allowed.");
            return Page();
        }

        var normalizedTags = ParseTags(Input.Tags)
            .Distinct()
            .ToList();

        if (normalizedTags.Count > 5)
        {
            ModelState.AddModelError(nameof(Input.Tags), "You can specify at most 5 tags.");
            return Page();
        }

        Input.Tags = normalizedTags.Count == 0
            ? null
            : string.Join(", ", normalizedTags);

        var invalidTag = normalizedTags.FirstOrDefault(tag => tag.Length is < 1 or > 32 || !IsValidTag(tag));
        if (invalidTag is not null)
        {
            ModelState.AddModelError(nameof(Input.Tags),
                $"Invalid tag '{invalidTag}'. Tags must be 1-32 characters and contain only letters, numbers, spaces, hyphens, or underscores.");
            return Page();
        }

        // ===== read, hash, buffer, scan =====
        string sha256Hex;
        await using var uploadStream = Input.File.OpenReadStream();
        await using var bufferedStream = new MemoryStream((int)Math.Min(Input.File.Length, int.MaxValue));
        using var sha = SHA256.Create();

        var headerBuffer = new byte[5];
        var headerBytesRead = await uploadStream.ReadAsync(headerBuffer.AsMemory(0, headerBuffer.Length), cancellationToken);
        if (headerBytesRead < headerBuffer.Length || !HasPdfHeader(headerBuffer.AsSpan(0, headerBytesRead)))
        {
            ModelState.AddModelError(nameof(Input.File), "Uploaded file is not a valid PDF.");
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
        if (bufferedStream.Length != Input.File.Length)
        {
            ModelState.AddModelError(nameof(Input.File), "Uploaded file could not be fully read. Please try again.");
            return Page();
        }

        bufferedStream.Position = 0;
        try
        {
            await _scanner.ScanOrThrowAsync(bufferedStream, cancellationToken);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(nameof(Input.File), string.IsNullOrWhiteSpace(ex.Message)
                ? "Upload blocked by security scan."
                : ex.Message);
            return Page();
        }
        finally
        {
            bufferedStream.Position = 0;
        }

        // ===== check for existing document with same hash (deleted or not) =====
        var existingWithSameHash = await _db.Documents
            .Include(d => d.DocumentTags)
            .FirstOrDefaultAsync(d => d.Sha256 == sha256Hex, cancellationToken);

        // save file to storage
        bufferedStream.Position = 0;
        var utcNow = DateTime.UtcNow;
        var storagePath = await _storage.SaveAsync(bufferedStream, Path.GetFileName(Input.File.FileName), utcNow, cancellationToken);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";

        if (existingWithSameHash is not null && !existingWithSameHash.IsDeleted)
        {
            // real duplicate, show message
            ModelState.AddModelError(nameof(Input.File),
                $"Duplicate document detected (matches {existingWithSameHash.Subject}). Upload blocked.");
            return Page();
        }

        if (existingWithSameHash is not null && existingWithSameHash.IsDeleted)
        {
            // RESTORE / REUSE the same row to avoid unique index violation
            existingWithSameHash.Subject = Input.Subject.Trim();
            existingWithSameHash.ReceivedFrom = string.IsNullOrWhiteSpace(Input.ReceivedFrom) ? null : Input.ReceivedFrom.Trim();
            existingWithSameHash.DocumentDate = Input.DocumentDate;
            existingWithSameHash.OfficeCategoryId = Input.OfficeCategoryId!.Value;
            existingWithSameHash.DocumentCategoryId = Input.DocumentCategoryId!.Value;
            existingWithSameHash.OriginalFileName = Path.GetFileName(Input.File.FileName);
            existingWithSameHash.FileSizeBytes = bufferedStream.Length;
            existingWithSameHash.StoragePath = storagePath;
            existingWithSameHash.MimeType = "application/pdf";
            existingWithSameHash.UpdatedAtUtc = utcNow;
            existingWithSameHash.IsDeleted = false;
            existingWithSameHash.IsActive = true;
            existingWithSameHash.OcrStatus = DocOcrStatus.Pending;
            existingWithSameHash.OcrFailureReason = null;

            // replace tags
            if (existingWithSameHash.DocumentTags is not null && existingWithSameHash.DocumentTags.Count > 0)
            {
                _db.DocumentTags.RemoveRange(existingWithSameHash.DocumentTags);
            }

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

                existingWithSameHash.DocumentTags = existingTags
                    .Where(t => normalizedTags.Contains(t.NormalizedName))
                    .Select(t => new DocumentTag
                    {
                        DocumentId = existingWithSameHash.Id,
                        TagId = t.Id
                    })
                    .ToList();
            }

            await _db.SaveChangesAsync(cancellationToken);

            await _audit.WriteAsync(existingWithSameHash.Id, userId, "Reuploaded",
                new { existingWithSameHash.Id, existingWithSameHash.Subject, sizeBytes = existingWithSameHash.FileSizeBytes }, cancellationToken);
            await _audit.WriteAsync(existingWithSameHash.Id, userId, "OcrQueued", new { existingWithSameHash.Id }, cancellationToken);

            TempData["ToastMessage"] = "Document re-uploaded.";
            return RedirectToPage("./Index");
        }

        // ===== no existing doc at all -> create new =====
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Subject = Input.Subject.Trim(),
            ReceivedFrom = string.IsNullOrWhiteSpace(Input.ReceivedFrom) ? null : Input.ReceivedFrom.Trim(),
            DocumentDate = Input.DocumentDate,
            OfficeCategoryId = Input.OfficeCategoryId!.Value,
            DocumentCategoryId = Input.DocumentCategoryId!.Value,
            OriginalFileName = Path.GetFileName(Input.File.FileName),
            FileSizeBytes = bufferedStream.Length,
            Sha256 = sha256Hex,
            StoragePath = storagePath,
            MimeType = "application/pdf",
            CreatedByUserId = userId,
            CreatedAtUtc = utcNow,
            OcrStatus = DocOcrStatus.Pending,
            OcrFailureReason = null
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

        await _audit.WriteAsync(document.Id, userId, "Uploaded", new { document.Id, document.Subject, sizeBytes = document.FileSizeBytes }, cancellationToken);
        await _audit.WriteAsync(document.Id, userId, "OcrQueued", new { document.Id }, cancellationToken);

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

        OfficeSelectList = new SelectList(OfficeOptions, nameof(OfficeCategory.Id), nameof(OfficeCategory.Name), Input.OfficeCategoryId);
        DocumentTypeSelectList = new SelectList(DocumentCategoryOptions, nameof(DocumentCategory.Id), nameof(DocumentCategory.Name), Input.DocumentCategoryId);
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

    private static IEnumerable<string> ParseTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return Array.Empty<string>();
        }

        return tags
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.ToLowerInvariant());
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
