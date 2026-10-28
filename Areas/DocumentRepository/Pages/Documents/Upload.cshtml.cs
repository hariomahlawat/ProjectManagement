using System;
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
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents;

[Authorize(Policy = "DocRepo.Upload")]
[EnableRateLimiting(DocumentUploadRateLimiting.PolicyName)]
public class UploadModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IDocStorage _storage;
    private readonly IFileScanner _scanner;
    private readonly IDocRepoAuditService _audit;
    private readonly IAuthorizationService _authorizationService;
    private readonly IDocRepoAccess _access;
    private readonly ProjectDocumentOptions _documentOptions;
    private readonly DocumentUploadRateLimitOptions _rateLimitOptions;

    public UploadModel(
        ApplicationDbContext db,
        IDocStorage storage,
        IFileScanner scanner,
        IDocRepoAuditService audit,
        IAuthorizationService authorizationService,
        IOptions<ProjectDocumentOptions> documentOptions,
        IOptions<DocumentUploadRateLimitOptions> rateLimitOptions,
        IDocRepoAccess access)
    {
        _access = access;
        _rateLimitOptions = rateLimitOptions.Value;
        _db = db;
        _storage = storage;
        _scanner = scanner;
        _audit = audit;
        _authorizationService = authorizationService;
        _documentOptions = documentOptions?.Value ?? throw new ArgumentNullException(nameof(documentOptions));
    }

    public IReadOnlyList<OfficeCategory> OfficeOptions { get; private set; } = Array.Empty<OfficeCategory>();
    public IReadOnlyList<DocumentCategory> DocumentCategoryOptions { get; private set; } = Array.Empty<DocumentCategory>();
    public SelectList OfficeSelectList { get; private set; } = default!;
    public SelectList DocumentTypeSelectList { get; private set; } = default!;
    public string? PdfPreviewUrl { get; private set; }
    public bool HasPreview => !string.IsNullOrEmpty(PdfPreviewUrl);
    public bool CanMarkAots { get; private set; }
    // SECTION: Upload constraints
    public long MaxFileSizeBytes => (long)_documentOptions.MaxSizeMb * 1024L * 1024L;

    public class InputModel
    {
        [Required, MaxLength(256)]
        public string Subject { get; set; } = string.Empty;

        [Display(Name = "Received from"), MaxLength(256)]
        public string? ReceivedFrom { get; set; }

        [Display(Name = "Document date"), DataType(DataType.Date)]
        public DateOnly? DocumentDate { get; set; }

        [Display(Name = "Office category"), Required]
        public int? OfficeCategoryId { get; set; }

        [Display(Name = "Document category"), Required]
        public int? DocumentCategoryId { get; set; }

        [Display(Name = "Tags (max 5, comma separated)"), MaxLength(128)]
        public string? Tags { get; set; }

        [Required]
        public IFormFile? File { get; set; }

        [Display(Name = "Mark as AOTS (All officers to see)")]
        public bool IsAots { get; set; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    // Model-state keys must match the bound field names ("Input.File"), or the page's
    // asp-validation-for messages never show the error.
    internal const string FileKey = nameof(Input) + "." + nameof(InputModel.File);
    internal const string TagsKey = nameof(Input) + "." + nameof(InputModel.Tags);

    // Set when a throttled upload was redirected back here (see DocumentUploadRateLimiting).
    public bool Throttled { get; private set; }

    public int ThrottleWindowMinutes => _rateLimitOptions.EffectiveWindowMinutes;

    public string ThrottleWaitText => ThrottleWindowMinutes == 1 ? "1 minute" : $"{ThrottleWindowMinutes} minutes";

    public async Task OnGetAsync(CancellationToken cancellationToken, bool throttled = false)
    {
        Throttled = throttled;
        CanMarkAots = (await _authorizationService.AuthorizeAsync(User, "DocRepo.EditMetadata")).Succeeded;
        await PopulateLookupsAsync(cancellationToken);
    }

    // SECTION: Duplicate pre-check
    /// <summary>
    /// Lets the page warn about a duplicate before a large file is sent. The browser hashes the
    /// chosen file (SHA-256) and asks whether the repository already holds it. The upload itself
    /// still checks, so this is only a convenience.
    /// </summary>
    public async Task<IActionResult> OnGetCheckHashAsync(string? sha256, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sha256) || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            return BadRequest();
        }

        var hash = sha256.ToUpperInvariant();
        var match = await _db.Documents
            .AsNoTracking()
            .Where(d => d.Sha256 == hash)
            .Select(d => new { d.Id, d.Subject, d.IsDeleted, d.IsExternal, d.IsActive })
            .FirstOrDefaultAsync(cancellationToken);

        if (match is null || match.IsDeleted)
        {
            // A trashed copy is restored by the upload, so it is not a duplicate.
            return new JsonResult(new { duplicate = false, restoresTrashed = match is not null });
        }

        var canSee = !match.IsExternal && (match.IsActive || await _access.CanViewInactiveAsync(User));
        return new JsonResult(new
        {
            duplicate = true,
            external = match.IsExternal,
            subject = canSee ? match.Subject : null,
            url = canSee ? Url.Page("./Reader", new { id = match.Id }) : null
        });
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var outcome = await SaveUploadAsync(cancellationToken);
        if (outcome.DocumentId is null)
        {
            return Page();
        }

        TempData["ToastMessage"] = outcome.Restored ? "Document re-uploaded." : "Document uploaded.";
        return RedirectToPage("./Index");
    }

    // SECTION: One file of a batch (the page uploads several PDFs one after another)
    // Same rules and checks as the form post; answers in JSON so the page can show each file's
    // result beside it.
    public async Task<IActionResult> OnPostJsonAsync(CancellationToken cancellationToken)
    {
        var outcome = await SaveUploadAsync(cancellationToken);
        if (outcome.DocumentId is not { } id)
        {
            var errors = ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(
                    entry => string.IsNullOrEmpty(entry.Key) ? "" : entry.Key.Replace("Input.", string.Empty, StringComparison.Ordinal),
                    entry => entry.Value!.Errors.Select(error => error.ErrorMessage).ToArray());
            return new JsonResult(new { ok = false, errors }) { StatusCode = StatusCodes.Status422UnprocessableEntity };
        }

        return new JsonResult(new { ok = true, id, restored = outcome.Restored, url = Url.Page("./Reader", new { id }) });
    }

    private sealed record UploadOutcome(Guid? DocumentId, bool Restored)
    {
        public static readonly UploadOutcome Failed = new(null, false);
    }

    private async Task<UploadOutcome> SaveUploadAsync(CancellationToken cancellationToken)
    {
        CanMarkAots = (await _authorizationService.AuthorizeAsync(User, "DocRepo.EditMetadata")).Succeeded;
        if (!CanMarkAots)
        {
            Input.IsAots = false;
        }

        await PopulateLookupsAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            return UploadOutcome.Failed;
        }

        if (Input.File is null || Input.File.Length == 0)
        {
            ModelState.AddModelError(FileKey, "PDF is required.");
            return UploadOutcome.Failed;
        }

        // SECTION: File size validation (configuration-driven)
        if (MaxFileSizeBytes > 0 && Input.File.Length > MaxFileSizeBytes)
        {
            var maxSizeMb = Math.Max(1, _documentOptions.MaxSizeMb);
            ModelState.AddModelError(FileKey, FormattableString.Invariant($"File exceeds the {maxSizeMb} MB limit."));
            return UploadOutcome.Failed;
        }

        var isPdfContentType = string.Equals(Input.File.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
        var hasPdfExtension = Path.GetExtension(Input.File.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdfContentType || !hasPdfExtension)
        {
            ModelState.AddModelError(FileKey, "Only PDF uploads are allowed.");
            return UploadOutcome.Failed;
        }

        var normalizedTags = ParseTags(Input.Tags)
            .Distinct()
            .ToList();

        if (normalizedTags.Count > 5)
        {
            ModelState.AddModelError(TagsKey, "You can specify at most 5 tags.");
            return UploadOutcome.Failed;
        }

        Input.Tags = normalizedTags.Count == 0
            ? null
            : string.Join(", ", normalizedTags);

        var invalidTag = normalizedTags.FirstOrDefault(tag => tag.Length is < 1 or > 32 || !IsValidTag(tag));
        if (invalidTag is not null)
        {
            ModelState.AddModelError(TagsKey,
                $"Invalid tag '{invalidTag}'. Tags must be 1-32 characters and contain only letters, numbers, spaces, hyphens, or underscores.");
            return UploadOutcome.Failed;
        }

        // SECTION: Read, hash and scan
        // The upload is copied to a private temp file while it is hashed, so large PDFs are never
        // held in memory. The file is deleted when the stream is closed.
        string sha256Hex;
        await using var uploadStream = Input.File.OpenReadStream();
        await using var bufferedStream = new FileStream(
            Path.Combine(Path.GetTempPath(), $"prism-docrepo-{Guid.NewGuid():N}.upload"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        using var sha = SHA256.Create();

        var headerBuffer = new byte[5];
        var headerBytesRead = await uploadStream.ReadAsync(headerBuffer.AsMemory(0, headerBuffer.Length), cancellationToken);
        if (headerBytesRead < headerBuffer.Length || !HasPdfHeader(headerBuffer.AsSpan(0, headerBytesRead)))
        {
            ModelState.AddModelError(FileKey, "Uploaded file is not a valid PDF.");
            return UploadOutcome.Failed;
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
            ModelState.AddModelError(FileKey, "Uploaded file could not be fully read. Please try again.");
            return UploadOutcome.Failed;
        }

        bufferedStream.Position = 0;
        try
        {
            await _scanner.ScanOrThrowAsync(bufferedStream, cancellationToken);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(FileKey, string.IsNullOrWhiteSpace(ex.Message)
                ? "Upload blocked by security scan."
                : ex.Message);
            return UploadOutcome.Failed;
        }

        // SECTION: Duplicate check (before anything is written to storage)
        var existingWithSameHash = await _db.Documents
            .Include(d => d.DocumentTags)
            .Include(d => d.DocumentText)
            .FirstOrDefaultAsync(d => d.Sha256 == sha256Hex, cancellationToken);

        if (existingWithSameHash is not null && !existingWithSameHash.IsDeleted)
        {
            ModelState.AddModelError(FileKey, existingWithSameHash.IsExternal
                ? "This PDF is already in the repository: it was added from another module."
                : $"Duplicate document detected (matches {existingWithSameHash.Subject}). Upload blocked.");
            return UploadOutcome.Failed;
        }

        // SECTION: Store the file
        bufferedStream.Position = 0;
        var utcNow = DateTime.UtcNow;
        var originalFileName = Path.GetFileName(Input.File.FileName);
        var storagePath = await _storage.SaveAsync(bufferedStream, originalFileName, utcNow, cancellationToken);
        var fileSize = bufferedStream.Length;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";

        Guid createdId;
        try
        {
            var tags = await ResolveTagsAsync(normalizedTags, cancellationToken);

            if (existingWithSameHash is not null)
            {
                // RESTORE / REUSE the trashed row with the same hash to avoid the unique index violation.
                var replacedStoragePath = existingWithSameHash.StoragePath;
                var document = existingWithSameHash;
                document.Subject = Input.Subject.Trim();
                document.ReceivedFrom = string.IsNullOrWhiteSpace(Input.ReceivedFrom) ? null : Input.ReceivedFrom.Trim();
                document.DocumentDate = Input.DocumentDate;
                document.OfficeCategoryId = Input.OfficeCategoryId!.Value;
                document.DocumentCategoryId = Input.DocumentCategoryId!.Value;
                document.OriginalFileName = originalFileName;
                document.FileSizeBytes = fileSize;
                document.StoragePath = storagePath;
                document.MimeType = "application/pdf";
                document.UpdatedAtUtc = utcNow;
                document.UpdatedByUserId = userId;
                document.IsDeleted = false;
                document.DeletedAtUtc = null;
                document.DeletedByUserId = null;
                document.DeleteReason = null;
                document.IsActive = true;
                document.IsExternal = false;
                document.OcrStatus = DocOcrStatus.Pending;
                document.OcrFailureReason = null;
                document.OcrLastTriedUtc = null;
                document.IsAots = Input.IsAots;

                document.DocumentTags.Clear();
                foreach (var tag in tags)
                {
                    document.DocumentTags.Add(new DocumentTag { DocumentId = document.Id, Tag = tag });
                }

                if (document.DocumentText is not null)
                {
                    document.DocumentText.OcrText = null;
                    document.DocumentText.UpdatedAtUtc = utcNow;
                }

                _audit.Stage(document.Id, userId, "Reuploaded", new { document.Id, document.Subject, sizeBytes = document.FileSizeBytes });
                _audit.Stage(document.Id, userId, "OcrQueued", new { document.Id });
                await _db.SaveChangesAsync(cancellationToken);

                // The trashed row's old file is no longer referenced by anything.
                await DeleteFileQuietlyAsync(replacedStoragePath, storagePath);

                return new UploadOutcome(document.Id, Restored: true);
            }

            var created = new Document
            {
                Id = Guid.NewGuid(),
                Subject = Input.Subject.Trim(),
                ReceivedFrom = string.IsNullOrWhiteSpace(Input.ReceivedFrom) ? null : Input.ReceivedFrom.Trim(),
                DocumentDate = Input.DocumentDate,
                OfficeCategoryId = Input.OfficeCategoryId!.Value,
                DocumentCategoryId = Input.DocumentCategoryId!.Value,
                OriginalFileName = originalFileName,
                FileSizeBytes = fileSize,
                Sha256 = sha256Hex,
                StoragePath = storagePath,
                MimeType = "application/pdf",
                CreatedByUserId = userId,
                CreatedAtUtc = utcNow,
                OcrStatus = DocOcrStatus.Pending,
                OcrFailureReason = null,
                IsAots = Input.IsAots
            };

            foreach (var tag in tags)
            {
                created.DocumentTags.Add(new DocumentTag { DocumentId = created.Id, Tag = tag });
            }

            _db.Documents.Add(created);
            _audit.Stage(created.Id, userId, "Uploaded", new { created.Id, created.Subject, sizeBytes = created.FileSizeBytes });
            _audit.Stage(created.Id, userId, "OcrQueued", new { created.Id });
            await _db.SaveChangesAsync(cancellationToken);
            createdId = created.Id;
        }
        catch (DbUpdateException)
        {
            // Nothing references the new file if the database write failed.
            await DeleteFileQuietlyAsync(storagePath, keepPath: null);
            if (!await _db.Documents.AsNoTracking().AnyAsync(d => d.Sha256 == sha256Hex && !d.IsDeleted, CancellationToken.None))
            {
                throw;
            }

            // Someone uploaded the same PDF at the same moment and their upload won.
            ModelState.AddModelError(FileKey, "This PDF was just uploaded by someone else. Upload blocked.");
            return UploadOutcome.Failed;
        }
        catch
        {
            // Nothing references the new file if the database write failed.
            await DeleteFileQuietlyAsync(storagePath, keepPath: null);
            throw;
        }

        return new UploadOutcome(createdId, Restored: false);
    }

    // SECTION: Tag helpers
    // Returns tracked tag entities; new ones are added to the context and saved with the document.
    private async Task<List<Tag>> ResolveTagsAsync(IReadOnlyCollection<string> normalizedTags, CancellationToken cancellationToken)
    {
        if (normalizedTags.Count == 0)
        {
            return new List<Tag>();
        }

        var tags = await _db.Tags
            .Where(t => normalizedTags.Contains(t.NormalizedName))
            .ToListAsync(cancellationToken);

        foreach (var name in normalizedTags.Except(tags.Select(t => t.NormalizedName)))
        {
            var tag = new Tag { Name = name, NormalizedName = name };
            _db.Tags.Add(tag);
            tags.Add(tag);
        }

        return tags;
    }

    private async Task DeleteFileQuietlyAsync(string? storagePath, string? keepPath)
    {
        if (string.IsNullOrWhiteSpace(storagePath) || string.Equals(storagePath, keepPath, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await _storage.DeleteAsync(storagePath, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the Missing files / storage report; never fail the user's upload for it.
        }
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
