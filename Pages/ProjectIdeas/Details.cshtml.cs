using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
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
    public bool CanEditCore { get; private set; }
    public bool ShowRestrictedEditNotice => CanEdit && !CanEditCore;
    public bool CanArchive { get; private set; }
    public bool CanRestore { get; private set; }
    public bool CanDelete { get; private set; }
    public bool CanAddComment { get; private set; }
    public bool CanAddConferenceComment { get; private set; }
    public bool CanAddNote { get; private set; }
    public bool CanUpload { get; private set; }
    public string DefaultCommentType { get; private set; } = ProjectIdeaCommentTypes.General;
    public bool IsArchived => Idea.Status == ProjectIdeaStatuses.Archived;
    public IReadOnlyList<ProjectIdeaDocument> Documents { get; private set; } = Array.Empty<ProjectIdeaDocument>();

    // SECTION: Bound form state
    [BindProperty, Required, MaxLength(4000)] public string CommentText { get; set; } = string.Empty;
    [BindProperty, MaxLength(32)] public string CommentType { get; set; } = ProjectIdeaCommentTypes.General;
    [BindProperty, Required, MaxLength(200)] public string NoteTitle { get; set; } = string.Empty;
    [BindProperty, Required] public string NoteBody { get; set; } = string.Empty;
    [BindProperty] public bool IsPinned { get; set; }
    [BindProperty, MaxLength(1000)] public string? ArchiveReason { get; set; }
    [BindProperty] public IFormFile? DocumentUpload { get; set; }
    [BindProperty(SupportsGet = true)] public bool OpenNoteComposer { get; set; }

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

        var comment = CommentText?.Trim();
        if (string.IsNullOrWhiteSpace(comment))
        {
            SetToastError("Remark cannot be empty.");
            return RedirectToDetails(id, "discussion");
        }

        if (comment.Length > 4000)
        {
            SetToastError("Remark cannot exceed 4,000 characters.");
            return RedirectToDetails(id, "discussion");
        }

        var commentType = ProjectIdeaCommentTypes.All.FirstOrDefault(type =>
            string.Equals(type, CommentType, StringComparison.OrdinalIgnoreCase));
        if (commentType is null)
        {
            return BadRequest("Invalid comment type.");
        }

        try
        {
            if (string.Equals(commentType, ProjectIdeaCommentTypes.Conference, StringComparison.Ordinal))
            {
                if (!_permissions.CanAddConferenceComment(User, Idea)) return Forbid();

                var actorRole = CurrentConferenceRole();
                if (actorRole is null) return Forbid();

                await _commands.AddConferenceCommentAsync(Idea, comment, CurrentUserId(), actorRole);
                SetToastSuccess("Conference direction added.");
            }
            else
            {
                await _commands.AddCommentAsync(Idea, comment, CurrentUserId());
                SetToastSuccess("Remark added.");
            }
        }
        catch (InvalidOperationException exception)
        {
            SetToastError(exception.Message);
        }

        return RedirectToDetails(id, "discussion");
    }

    public async Task<IActionResult> OnPostEditCommentAsync(
        int id,
        int commentId,
        string? editedCommentText,
        string? rowVersion)
    {
        if (!await LoadAsync(id)) return NotFound();
        var comment = Idea.Comments.FirstOrDefault(candidate => candidate.Id == commentId && !candidate.IsDeleted);
        if (comment is null) return NotFound();
        if (!_permissions.CanEditComment(User, Idea, comment)) return Forbid();

        try
        {
            var updated = await _commands.EditCommentAsync(
                id,
                commentId,
                editedCommentText,
                DecodeRowVersion(rowVersion),
                CurrentActor());
            if (updated is null) return NotFound();
            SetToastSuccess(string.Equals(updated.CommentType, ProjectIdeaCommentTypes.Conference, StringComparison.OrdinalIgnoreCase)
                ? "Conference direction updated."
                : "Remark updated.");
        }
        catch (InvalidOperationException exception)
        {
            SetToastError(exception.Message);
        }

        return RedirectToDetails(id, "discussion");
    }

    public async Task<IActionResult> OnPostDeleteCommentAsync(
        int id,
        int commentId,
        string? rowVersion)
    {
        if (!await LoadAsync(id)) return NotFound();
        var comment = Idea.Comments.FirstOrDefault(candidate => candidate.Id == commentId && !candidate.IsDeleted);
        if (comment is null) return NotFound();
        if (!_permissions.CanDeleteComment(User, Idea, comment)) return Forbid();

        try
        {
            var deleted = await _commands.SoftDeleteCommentAsync(
                id,
                commentId,
                DecodeRowVersion(rowVersion),
                CurrentActor());
            if (!deleted) return NotFound();
            SetToastSuccess(string.Equals(comment.CommentType, ProjectIdeaCommentTypes.Conference, StringComparison.OrdinalIgnoreCase)
                ? "Conference direction deleted."
                : "Remark deleted.");
        }
        catch (InvalidOperationException exception)
        {
            SetToastError(exception.Message);
        }

        return RedirectToDetails(id, "discussion");
    }

    public async Task<IActionResult> OnPostNoteAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!_permissions.CanAddNote(User, Idea)) return Forbid();

        var title = NoteTitle?.Trim();
        var body = NoteBody?.Trim();
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
        {
            SetToastError("Note title and body are required.");
            return RedirectToDetails(id, "notes", openNoteComposer: true);
        }

        if (title.Length > 200)
        {
            SetToastError("Note title cannot exceed 200 characters.");
            return RedirectToDetails(id, "notes", openNoteComposer: true);
        }

        try
        {
            await _commands.AddNoteAsync(Idea, title, body, IsPinned, CurrentUserId());
            SetToastSuccess("Idea note added.");
        }
        catch (InvalidOperationException exception)
        {
            SetToastError(exception.Message);
        }
        return RedirectToDetails(id, "notes");
    }

    public async Task<IActionResult> OnPostArchiveAsync(int id, string? rowVersion)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!_permissions.CanArchiveIdea(User) || IsArchived) return Forbid();
        var archiveReason = ArchiveReason?.Trim();
        if (string.IsNullOrWhiteSpace(archiveReason))
        {
            SetToastError("Please enter a closing note or reason before archiving the idea.");
            return RedirectToDetails(id);
        }

        if (archiveReason.Length > 1000)
        {
            SetToastError("The closing note cannot exceed 1,000 characters.");
            return RedirectToDetails(id);
        }

        try
        {
            await _commands.ArchiveAsync(Idea, archiveReason, DecodeRowVersion(rowVersion));
            SetToastSuccess("Idea archived.");
        }
        catch (InvalidOperationException exception)
        {
            SetToastError(exception.Message);
        }
        return RedirectToDetails(id);
    }

    public async Task<IActionResult> OnPostRestoreAsync(int id, string? rowVersion)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!_permissions.CanRestoreIdea(User)) return Forbid();
        try
        {
            await _commands.RestoreAsync(Idea, DecodeRowVersion(rowVersion));
            SetToastSuccess("Idea restored.");
        }
        catch (InvalidOperationException exception)
        {
            SetToastError(exception.Message);
        }
        return RedirectToDetails(id);
    }

    public async Task<IActionResult> OnPostDeleteAsync(
        int id,
        string? deleteReason,
        string? rowVersion)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!_permissions.CanDeleteIdea(User)) return Forbid();

        try
        {
            var deleted = await _commands.SoftDeleteIdeaAsync(
                id,
                deleteReason,
                DecodeRowVersion(rowVersion),
                CurrentActor());
            if (!deleted) return NotFound();
            SetToastSuccess("Idea deleted.");
            return RedirectToPage("Deleted");
        }
        catch (InvalidOperationException exception)
        {
            SetToastError(exception.Message);
            return RedirectToDetails(id);
        }
    }

    public async Task<IActionResult> OnPostUploadAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!_permissions.CanUploadDocument(User, Idea)) return Forbid();
        if (DocumentUpload is null) { SetToastError("Please select a document to upload."); return RedirectToDetails(id, "documents"); }
        var result = await _documents.UploadAsync(Idea, DocumentUpload, CurrentUserId());
        if (!result.Success) { SetToastError(result.Error ?? "Document upload failed."); return RedirectToDetails(id, "documents"); }
        SetToastSuccess("Document uploaded successfully.");
        return RedirectToDetails(id, "documents");
    }

    public async Task<IActionResult> OnPostDeleteDocumentAsync(int id, int documentId)
    {
        if (!await LoadAsync(id)) return NotFound();
        var doc = await _documents.GetAsync(documentId);
        if (doc is null || doc.ProjectIdeaId != id || doc.IsDeleted) return NotFound();
        if (!_permissions.CanDeleteDocument(User, doc, Idea)) return Forbid();
        try
        {
            await _documents.SoftDeleteAsync(doc);
            SetToastSuccess("Document deleted.");
        }
        catch (InvalidOperationException exception)
        {
            SetToastError(exception.Message);
        }
        return RedirectToDetails(id, "documents");
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
    public bool CanEditComment(ProjectIdeaComment comment) => _permissions.CanEditComment(User, Idea, comment);
    public bool CanDeleteComment(ProjectIdeaComment comment) => _permissions.CanDeleteComment(User, Idea, comment);
    public static string EncodeRowVersion(byte[]? rowVersion) => rowVersion is { Length: > 0 } ? Convert.ToBase64String(rowVersion) : string.Empty;

    public string DisplayUser(ProjectManagement.Models.ApplicationUser? user, string fallback = "Unknown") =>
        user?.FullName ?? user?.UserName ?? user?.Email ?? fallback;

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

    public static string DisplayCommentType(string? commentType)
        => string.Equals(commentType, ProjectIdeaCommentTypes.Conference, StringComparison.OrdinalIgnoreCase)
            ? "Conference"
            : "General";

    public static string DisplayRole(string? role)
    {
        if (string.Equals(role, RoleNames.Comdt, StringComparison.OrdinalIgnoreCase)) return "Comdt";
        if (string.Equals(role, RoleNames.HoD, StringComparison.OrdinalIgnoreCase)) return "HoD";
        return role?.Trim() ?? string.Empty;
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
        CanEditCore = _permissions.CanEditIdeaCore(User, idea);
        CanArchive = _permissions.CanArchiveIdea(User);
        CanRestore = _permissions.CanRestoreIdea(User);
        CanDelete = _permissions.CanDeleteIdea(User);
        CanAddComment = _permissions.CanAddComment(User, idea);
        CanAddConferenceComment = _permissions.CanAddConferenceComment(User, idea);
        CanAddNote = _permissions.CanAddNote(User, idea);
        CanUpload = _permissions.CanUploadDocument(User, idea);
        DefaultCommentType = _permissions.GetDefaultCommentType(User, idea);
        Documents = idea.Documents
            .Where(d => !d.IsDeleted)
            .OrderByDescending(d => d.UploadedAt)
            .ToList();
        return true;
    }

    private void SetToastSuccess(string message) => TempData["ToastSuccess"] = message;

    private void SetToastError(string message) => TempData["ToastError"] = message;

    private IActionResult RedirectToDetails(int id, string? fragment = null, bool openNoteComposer = false)
    {
        var url = openNoteComposer
            ? Url.Page("Details", new { id, openNoteComposer = true })
            : Url.Page("Details", new { id });

        url ??= $"/ProjectIdeas/Details/{id}";
        return string.IsNullOrWhiteSpace(fragment)
            ? Redirect(url)
            : Redirect($"{url}#{fragment}");
    }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private ProjectIdeaActorContext CurrentActor()
        => new(
            CurrentUserId(),
            User.FindAll(ClaimTypes.Role).Select(claim => claim.Value));

    private static byte[] DecodeRowVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(ProjectIdeaCommandService.RowVersionRequiredMessage);
        }

        try
        {
            var decoded = Convert.FromBase64String(value);
            return decoded.Length > 0
                ? decoded
                : throw new InvalidOperationException(ProjectIdeaCommandService.RowVersionRequiredMessage);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(ProjectIdeaCommandService.RowVersionRequiredMessage, exception);
        }
    }

    private string? CurrentConferenceRole()
    {
        if (User.IsInRole(RoleNames.Comdt)) return RoleNames.Comdt;
        if (User.IsInRole(RoleNames.HoD)) return RoleNames.HoD;
        return null;
    }
}
