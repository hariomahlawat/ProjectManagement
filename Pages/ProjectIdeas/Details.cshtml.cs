using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Services.ProjectIdeas;

namespace ProjectManagement.Pages.ProjectIdeas;

[Authorize]
public class DetailsModel : PageModel
{
    private readonly ProjectIdeaReadService _read;
    private readonly ProjectIdeaCommandService _commands;
    private readonly ProjectIdeaPermissionService _permissions;
    private readonly ProjectIdeaDocumentService _documents;

    public DetailsModel(ProjectIdeaReadService read, ProjectIdeaCommandService commands, ProjectIdeaPermissionService permissions, ProjectIdeaDocumentService documents)
    {
        _read = read;
        _commands = commands;
        _permissions = permissions;
        _documents = documents;
    }

    // SECTION: Page state
    public ProjectIdea Idea { get; private set; } = default!;
    public bool CanEdit { get; private set; }
    public bool CanArchive { get; private set; }
    public bool CanRestore { get; private set; }
    public bool CanAddComment { get; private set; }
    public bool CanAddNote { get; private set; }
    public bool CanUpload { get; private set; }
    public bool IsArchived => Idea.Status == ProjectIdeaStatuses.Archived;
    public IReadOnlyList<ProjectIdeaDocument> Documents { get; private set; } = Array.Empty<ProjectIdeaDocument>();

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    // SECTION: Bound form state
    [BindProperty, Required, MaxLength(4000)] public string CommentText { get; set; } = string.Empty;
    [BindProperty, Required, MaxLength(200)] public string NoteTitle { get; set; } = string.Empty;
    [BindProperty, Required] public string NoteBody { get; set; } = string.Empty;
    [BindProperty] public bool IsPinned { get; set; }
    [BindProperty, MaxLength(1000)] public string? ArchiveReason { get; set; }
    [BindProperty] public IFormFile? DocumentUpload { get; set; }

    // SECTION: Page handlers
    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!_permissions.CanViewIdea(User, Idea)) return Forbid();
        return Page();
    }

    public async Task<IActionResult> OnPostCommentAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!_permissions.CanAddComment(User, Idea)) return Forbid();
        if (string.IsNullOrWhiteSpace(CommentText)) { ErrorMessage = "Comment cannot be empty."; return RedirectToPage(new { id }); }
        await _commands.AddCommentAsync(Idea, CommentText.Trim(), CurrentUserId());
        StatusMessage = "Comment added.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostNoteAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!_permissions.CanAddNote(User, Idea)) return Forbid();
        if (string.IsNullOrWhiteSpace(NoteTitle) || string.IsNullOrWhiteSpace(NoteBody)) { ErrorMessage = "Note title and body are required."; return RedirectToPage(new { id }); }
        await _commands.AddNoteAsync(Idea, NoteTitle.Trim(), NoteBody.Trim(), IsPinned, CurrentUserId());
        StatusMessage = "Note added.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostArchiveAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!_permissions.CanArchiveIdea(User) || IsArchived) return Forbid();
        if (string.IsNullOrWhiteSpace(ArchiveReason))
        {
            ErrorMessage = "Please enter a closing note or reason before archiving the idea.";
            return RedirectToPage(new { id });
        }

        await _commands.ArchiveAsync(Idea, ArchiveReason.Trim());
        StatusMessage = "Idea archived.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRestoreAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!_permissions.CanRestoreIdea(User)) return Forbid();
        await _commands.RestoreAsync(Idea);
        StatusMessage = "Idea restored.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUploadAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!_permissions.CanUploadDocument(User, Idea)) return Forbid();
        if (DocumentUpload is null) { ErrorMessage = "Please select a document to upload."; return RedirectToPage(new { id }); }
        var result = await _documents.UploadAsync(Idea, DocumentUpload, CurrentUserId());
        if (!result.Success) { ErrorMessage = result.Error ?? "Document upload failed."; return RedirectToPage(new { id }); }
        StatusMessage = "Document uploaded successfully.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteDocumentAsync(int id, int documentId)
    {
        if (!await LoadAsync(id)) return NotFound();
        var doc = await _documents.GetAsync(documentId);
        if (doc is null || doc.ProjectIdeaId != id || doc.IsDeleted) return NotFound();
        if (!_permissions.CanDeleteDocument(User, doc, Idea)) return Forbid();
        await _documents.SoftDeleteAsync(doc);
        StatusMessage = "Document deleted.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetPreviewAsync(int id, int documentId)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!_permissions.CanViewIdea(User, Idea)) return Forbid();

        var document = await _documents.GetAsync(documentId);
        if (document is null || document.ProjectIdeaId != id || document.IsDeleted) return NotFound();

        if (!IsImage(document) && !IsPdf(document))
        {
            return BadRequest("Preview is available only for PDF and image files.");
        }

        string absolutePath;
        try { absolutePath = _documents.GetAbsolutePath(document); }
        catch (InvalidOperationException) { return NotFound(); }

        if (!System.IO.File.Exists(absolutePath)) return NotFound();

        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return CreatePhysicalFileResult(absolutePath, GetPreviewContentType(document));
    }

    public async Task<IActionResult> OnGetDownloadAsync(int id, int documentId)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!_permissions.CanViewIdea(User, Idea)) return Forbid();

        var document = await _documents.GetAsync(documentId);
        if (document is null || document.ProjectIdeaId != id || document.IsDeleted) return NotFound();

        string absolutePath;
        try { absolutePath = _documents.GetAbsolutePath(document); }
        catch (InvalidOperationException) { return NotFound(); }

        if (!System.IO.File.Exists(absolutePath)) return NotFound();

        var contentType = string.IsNullOrWhiteSpace(document.ContentType) ? "application/octet-stream" : document.ContentType;
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return CreatePhysicalFileResult(absolutePath, contentType, document.OriginalFileName);
    }

    // SECTION: File response helpers
    private static PhysicalFileResult CreatePhysicalFileResult(string absolutePath, string contentType, string? downloadName = null)
    {
        return new PhysicalFileResult(absolutePath, contentType)
        {
            EnableRangeProcessing = true,
            FileDownloadName = downloadName
        };
    }

    // SECTION: Attachment view helpers
    public bool CanDeleteDocument(ProjectIdeaDocument document) => _permissions.CanDeleteDocument(User, document, Idea);

    public string DisplayUser(ProjectManagement.Models.ApplicationUser user) => user.FullName ?? user.UserName ?? user.Email ?? "Unknown";

    public static bool IsImage(ProjectIdeaDocument document)
    {
        var extension = Path.GetExtension(document.OriginalFileName);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPdf(ProjectIdeaDocument document)
    {
        var extension = Path.GetExtension(document.OriginalFileName);
        return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public static string FileExtension(ProjectIdeaDocument document)
    {
        var extension = Path.GetExtension(document.OriginalFileName);
        return string.IsNullOrWhiteSpace(extension) ? "FILE" : extension.TrimStart('.').ToUpperInvariant();
    }

    public static string FileIcon(ProjectIdeaDocument document)
    {
        var extension = Path.GetExtension(document.OriginalFileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "bi-file-earmark-pdf",
            ".doc" or ".docx" => "bi-file-earmark-word",
            ".xls" or ".xlsx" => "bi-file-earmark-excel",
            ".ppt" or ".pptx" => "bi-file-earmark-ppt",
            ".png" or ".jpg" or ".jpeg" => "bi-file-earmark-image",
            _ => "bi-file-earmark"
        };
    }

    public static string FileTypeClass(ProjectIdeaDocument document)
    {
        var extension = Path.GetExtension(document.OriginalFileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "pdf",
            ".doc" or ".docx" => "word",
            ".xls" or ".xlsx" => "excel",
            ".ppt" or ".pptx" => "ppt",
            ".png" or ".jpg" or ".jpeg" => "image",
            _ => "file"
        };
    }

    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024d;
        if (kb < 1024) return $"{kb:0.#} KB";
        double mb = kb / 1024d;
        return $"{mb:0.#} MB";
    }

    private static string GetPreviewContentType(ProjectIdeaDocument document)
    {
        var extension = Path.GetExtension(document.OriginalFileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
    }

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "U";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "U";
        if (parts.Length == 1) return char.ToUpperInvariant(parts[0][0]).ToString();
        return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    // SECTION: Internal loading
    private async Task<bool> LoadAsync(int id)
    {
        var idea = await _read.GetDetailsAsync(id);
        if (idea is null) return false;
        Idea = idea;
        CanEdit = _permissions.CanEditIdea(User, idea);
        CanArchive = _permissions.CanArchiveIdea(User);
        CanRestore = _permissions.CanRestoreIdea(User);
        CanAddComment = _permissions.CanAddComment(User, idea);
        CanAddNote = _permissions.CanAddNote(User, idea);
        CanUpload = _permissions.CanUploadDocument(User, idea);
        Documents = idea.Documents
            .Where(d => !d.IsDeleted)
            .OrderByDescending(d => d.UploadedAt)
            .ToList();
        return true;
    }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
