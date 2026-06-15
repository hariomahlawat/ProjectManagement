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
    private readonly ProjectIdeaReadService _read; private readonly ProjectIdeaCommandService _commands; private readonly ProjectIdeaPermissionService _permissions; private readonly ProjectIdeaDocumentService _documents;
    public DetailsModel(ProjectIdeaReadService read, ProjectIdeaCommandService commands, ProjectIdeaPermissionService permissions, ProjectIdeaDocumentService documents) { _read = read; _commands = commands; _permissions = permissions; _documents = documents; }
    public ProjectIdea Idea { get; private set; } = default!; public bool CanEdit { get; private set; } public bool CanArchive { get; private set; } public bool CanAddComment { get; private set; } public bool CanAddNote { get; private set; } public bool CanUpload { get; private set; }
    [BindProperty, Required, MaxLength(4000)] public string CommentText { get; set; } = string.Empty;
    [BindProperty, Required, MaxLength(200)] public string NoteTitle { get; set; } = string.Empty;
    [BindProperty, Required] public string NoteBody { get; set; } = string.Empty;
    [BindProperty] public bool IsPinned { get; set; }
    [BindProperty, MaxLength(1000)] public string? ArchiveReason { get; set; }
    [BindProperty] public IFormFile? DocumentUpload { get; set; }
    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();
    public async Task<IActionResult> OnPostCommentAsync(int id) { if (!await LoadAsync(id)) return NotFound(); if (!CanAddComment) return Forbid(); if (string.IsNullOrWhiteSpace(CommentText)) return RedirectToPage(new { id }); await _commands.AddCommentAsync(Idea, CommentText.Trim(), CurrentUserId()); return RedirectToPage(new { id }); }
    public async Task<IActionResult> OnPostNoteAsync(int id) { if (!await LoadAsync(id)) return NotFound(); if (!CanAddNote) return Forbid(); if (string.IsNullOrWhiteSpace(NoteTitle) || string.IsNullOrWhiteSpace(NoteBody)) return RedirectToPage(new { id }); await _commands.AddNoteAsync(Idea, NoteTitle.Trim(), NoteBody.Trim(), IsPinned, CurrentUserId()); return RedirectToPage(new { id }); }
    public async Task<IActionResult> OnPostArchiveAsync(int id) { if (!await LoadAsync(id)) return NotFound(); if (!CanArchive) return Forbid(); await _commands.ArchiveAsync(Idea, ArchiveReason); return RedirectToPage(new { id }); }
    public async Task<IActionResult> OnPostRestoreAsync(int id) { if (!await LoadAsync(id)) return NotFound(); if (!CanArchive) return Forbid(); await _commands.RestoreAsync(Idea); return RedirectToPage(new { id }); }
    public async Task<IActionResult> OnPostUploadAsync(int id) { if (!await LoadAsync(id)) return NotFound(); if (!CanUpload) return Forbid(); if (DocumentUpload is not null) await _documents.UploadAsync(Idea, DocumentUpload, CurrentUserId()); return RedirectToPage(new { id }); }
    public async Task<IActionResult> OnPostDeleteDocumentAsync(int id, int documentId) { if (!await LoadAsync(id)) return NotFound(); var doc = Idea.Documents.FirstOrDefault(x => x.Id == documentId); if (doc is null) return NotFound(); if (!_permissions.CanDeleteDocument(User, doc, Idea)) return Forbid(); await _documents.SoftDeleteAsync(doc); return RedirectToPage(new { id }); }
    public async Task<IActionResult> OnGetDownloadAsync(int id, int documentId) { if (!await LoadAsync(id)) return NotFound(); var doc = await _documents.GetAsync(documentId); if (doc is null || doc.ProjectIdeaId != id) return NotFound(); var path = _documents.GetAbsolutePath(doc); return PhysicalFile(path, doc.ContentType ?? "application/octet-stream", doc.OriginalFileName); }
    private async Task<bool> LoadAsync(int id) { var idea = await _read.GetDetailsAsync(id); if (idea is null) return false; Idea = idea; CanEdit = _permissions.CanEditIdea(User, idea); CanArchive = _permissions.CanArchiveIdea(User); CanAddComment = _permissions.CanAddComment(User, idea); CanAddNote = _permissions.CanAddNote(User, idea); CanUpload = _permissions.CanUploadDocument(User, idea); return true; }
    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
