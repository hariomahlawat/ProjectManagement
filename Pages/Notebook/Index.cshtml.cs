using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Models;
using ProjectManagement.Services.Notebook;
using ProjectManagement.ViewModels.Notebook;

namespace ProjectManagement.Pages.Notebook;

[Authorize]
public class IndexModel : PageModel
{
    private readonly INotebookService _notebook;
    private readonly INotebookTodoImportService _import;
    private readonly UserManager<ApplicationUser> _users;

    public IndexModel(INotebookService notebook, INotebookTodoImportService import, UserManager<ApplicationUser> users)
    {
        _notebook = notebook;
        _import = import;
        _users = users;
    }

    // SECTION: Bound notebook state
    [BindProperty(SupportsGet = true)] public string View { get; set; } = "home";
    [BindProperty(SupportsGet = true)] public string? Query { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? SelectedId { get; set; }
    [BindProperty] public string? QuickCaptureText { get; set; }
    [BindProperty] public NotebookItemType? ForcedType { get; set; }
    [BindProperty] public NotebookEditInput Input { get; set; } = new();
    [BindProperty] public string? TagsText { get; set; }
    [BindProperty] public string? ChecklistText { get; set; }
    public NotebookIndexVm Notebook { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var uid = _users.GetUserId(User); if (uid is null) return Unauthorized();
        await _import.ImportForUserIfRequiredAsync(uid, ct);
        Notebook = await _notebook.GetIndexAsync(uid, View, Query, SelectedId, ct);
        PopulateEditorInput();
        return Page();
    }

    // SECTION: Thin post handlers
    public async Task<IActionResult> OnPostQuickCaptureAsync(CancellationToken ct) { var uid = _users.GetUserId(User); if (uid is null) return Unauthorized(); if (!string.IsNullOrWhiteSpace(QuickCaptureText)) SelectedId = await _notebook.QuickCaptureAsync(uid, QuickCaptureText, ForcedType, ct); return RedirectToCurrent(SelectedId); }
    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct) { var uid = _users.GetUserId(User); if (uid is null) return Unauthorized(); HydrateInput(); SelectedId = await _notebook.CreateAsync(uid, Input, ct); return RedirectToCurrent(SelectedId); }
    public async Task<IActionResult> OnPostUpdateAsync(Guid id, CancellationToken ct) { var uid = _users.GetUserId(User); if (uid is null) return Unauthorized(); HydrateInput(); await _notebook.UpdateAsync(uid, id, Input, ct); return RedirectToCurrent(id); }
    public async Task<IActionResult> OnPostArchiveAsync(Guid id, CancellationToken ct) { var uid = _users.GetUserId(User); if (uid is null) return Unauthorized(); await _notebook.ArchiveAsync(uid, id, ct); return RedirectToCurrent(); }
    public async Task<IActionResult> OnPostRestoreAsync(Guid id, CancellationToken ct) { var uid = _users.GetUserId(User); if (uid is null) return Unauthorized(); await _notebook.RestoreAsync(uid, id, ct); return RedirectToPage(new { view = "archived", query = Query, selectedId = id }); }
    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct) { var uid = _users.GetUserId(User); if (uid is null) return Unauthorized(); await _notebook.DeleteAsync(uid, id, ct); return RedirectToCurrent(); }
    public async Task<IActionResult> OnPostTogglePinAsync(Guid id, CancellationToken ct) { var uid = _users.GetUserId(User); if (uid is null) return Unauthorized(); await _notebook.TogglePinAsync(uid, id, ct); return RedirectToCurrent(SelectedId ?? id); }
    public async Task<IActionResult> OnPostToggleFavoriteAsync(Guid id, CancellationToken ct) { var uid = _users.GetUserId(User); if (uid is null) return Unauthorized(); await _notebook.ToggleFavoriteAsync(uid, id, ct); return RedirectToCurrent(SelectedId ?? id); }
    public async Task<IActionResult> OnPostCompleteAsync(Guid id, bool isComplete, CancellationToken ct) { var uid = _users.GetUserId(User); if (uid is null) return Unauthorized(); await _notebook.CompleteAsync(uid, id, isComplete, ct); return RedirectToCurrent(SelectedId ?? id); }
    public async Task<IActionResult> OnPostConvertAsync(Guid id, NotebookItemType newType, CancellationToken ct) { var uid = _users.GetUserId(User); if (uid is null) return Unauthorized(); await _notebook.ConvertTypeAsync(uid, id, newType, ct); return RedirectToCurrent(id); }
    public async Task<IActionResult> OnPostToggleChecklistItemAsync(int checklistItemId, bool isDone, Guid selectedId, CancellationToken ct) { var uid = _users.GetUserId(User); if (uid is null) return Unauthorized(); await _notebook.ToggleChecklistItemAsync(uid, checklistItemId, isDone, ct); return RedirectToCurrent(selectedId); }

    private void PopulateEditorInput()
    {
        var selected = Notebook.SelectedItem;
        if (selected is null) return;
        Input = new NotebookEditInput
        {
            Title = selected.Title,
            BodyMarkdown = selected.BodyMarkdown,
            Type = selected.Type,
            Priority = selected.Priority,
            ReminderAtUtc = selected.ReminderAtUtc,
            ReminderLocal = NotebookReminderFormatter.ToLocalInput(selected.ReminderAtUtc),
            IsPinned = selected.IsPinned,
            IsFavorite = selected.IsFavorite,
            ColorKey = selected.ColorKey,
            Tags = selected.Tags,
            ChecklistItems = selected.ChecklistItems.Select(x => x.Text).ToArray()
        };
        TagsText = string.Join(", ", selected.Tags);
        ChecklistText = string.Join("\n", selected.ChecklistItems.Select(x => x.Text));
    }

    private void HydrateInput()
    {
        Input.ReminderAtUtc = NotebookReminderFormatter.FromLocalInput(Input.ReminderLocal);
        Input.Tags = (TagsText ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Input.ChecklistItems = (ChecklistText ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private IActionResult RedirectToCurrent(Guid? selectedId = null)
    {
        return RedirectToPage(new
        {
            view = View,
            query = Query,
            selectedId
        });
    }
}
