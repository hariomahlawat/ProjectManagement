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
    [BindProperty(SupportsGet = true)]
    public string View { get; set; } = "home";
    [BindProperty(SupportsGet = true)]
    public string? Query { get; set; }
    [BindProperty(SupportsGet = true)]
    public Guid? SelectedId { get; set; }
    [BindProperty(SupportsGet = true)]
    public Guid? Note { get; set; }
    [BindProperty(SupportsGet = true)]
    public string? Mode { get; set; }
    [BindProperty(SupportsGet = true)]
    public NotebookItemType? Type { get; set; }
    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }
    [BindProperty(SupportsGet = true)]
    public string? Tag { get; set; }
    [BindProperty]
    public string? QuickCaptureText { get; set; }
    [BindProperty]
    public NotebookItemType? ForcedType { get; set; }
    [BindProperty]
    public NotebookEditInput Input { get; set; } = new();
    [BindProperty]
    public string? TagsText { get; set; }
    [BindProperty]
    public string? ChecklistText { get; set; }
    public NotebookIndexVm Notebook { get; set; } = new();
    public bool UseLegacyEditor => SelectedId.HasValue || IsCreateMode();
    public bool HasEditorOpen => UseLegacyEditor;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var uid = _users.GetUserId(User);
        if (uid is null)
        {
            return Unauthorized();
        }
        await _import.ImportForUserIfRequiredAsync(uid, ct);
        if (SelectedId.HasValue)
        {
            return RedirectToPage(new { note = SelectedId, view = View, query = Query, filter = Filter, tag = Tag });
        }

        // SECTION: The modern note query opens the JavaScript modal only.
        SelectedId = null;
        NormalizeLegacyTypeView();
        var isCreateMode = IsCreateMode();
        Notebook = await _notebook.GetIndexAsync(uid, View, Query, Filter, Tag, SelectedId, ct);
        PopulateEditorInput();
        return Page();
    }

    // SECTION: Thin post handlers
    public async Task<IActionResult> OnPostQuickCaptureAsync(CancellationToken ct)
    {
        var uid = _users.GetUserId(User);
        if (uid is null)
        {
            return Unauthorized();
        }

        if (!string.IsNullOrWhiteSpace(QuickCaptureText))
        {
            SelectedId = await _notebook.QuickCaptureAsync(uid, QuickCaptureText, ForcedType, ct);
        }

        return RedirectToCurrent(SelectedId);
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        var uid = _users.GetUserId(User);
        if (uid is null)
        {
            return Unauthorized();
        }

        HydrateInput();
        SelectedId = (await _notebook.CreateAsync(uid, Input, ct)).Id;
        return RedirectToCurrent(SelectedId);
    }

    public async Task<IActionResult> OnPostUpdateAsync(Guid id, Guid expectedVersion, CancellationToken ct)
    {
        var uid = _users.GetUserId(User);
        if (uid is null)
        {
            return Unauthorized();
        }

        HydrateInput();
        await _notebook.UpdateAsync(uid, id, Input, expectedVersion, ct);
        return RedirectToCurrent(id);
    }

    public async Task<IActionResult> OnPostArchiveAsync(Guid id, Guid expectedVersion, CancellationToken ct)
    {
        var uid = _users.GetUserId(User);
        if (uid is null)
        {
            return Unauthorized();
        }

        if (expectedVersion == Guid.Empty) return BadRequest();
        await _notebook.ArchiveAsync(uid, id, expectedVersion, ct);
        return RedirectToCurrent();
    }

    public async Task<IActionResult> OnPostRestoreAsync(Guid id, Guid expectedVersion, CancellationToken ct)
    {
        var uid = _users.GetUserId(User);
        if (uid is null)
        {
            return Unauthorized();
        }

        if (expectedVersion == Guid.Empty) return BadRequest();
        await _notebook.RestoreAsync(uid, id, expectedVersion, ct);
        return RedirectToPage(new { view = "archive", query = Query, note = id });
    }


    public async Task<IActionResult> OnPostDuplicateAsync(Guid id, CancellationToken ct)
    {
        var uid = _users.GetUserId(User);
        if (uid is null)
        {
            return Unauthorized();
        }

        var copy = await _notebook.DuplicateAsync(uid, id, ct);
        return RedirectToCurrent(copy.Id);
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, Guid expectedVersion, CancellationToken ct)
    {
        var uid = _users.GetUserId(User);
        if (uid is null)
        {
            return Unauthorized();
        }

        if (expectedVersion == Guid.Empty) return BadRequest();
        await _notebook.DeleteAsync(uid, id, expectedVersion, ct);
        return RedirectToCurrent();
    }

    public async Task<IActionResult> OnPostTogglePinAsync(Guid id, Guid expectedVersion, CancellationToken ct)
    {
        var uid = _users.GetUserId(User);
        if (uid is null)
        {
            return Unauthorized();
        }

        if (expectedVersion == Guid.Empty) return BadRequest();
        var current = await _notebook.GetDetailAsync(uid, id, ct);
        if (current is null) return NotFound();
        await _notebook.SetPinnedAsync(uid, id, !current.IsPinned, expectedVersion, ct);
        return RedirectToCurrent(SelectedId);
    }

    public async Task<IActionResult> OnPostCompleteAsync(Guid id, bool isComplete, Guid expectedVersion, CancellationToken ct)
    {
        var uid = _users.GetUserId(User);
        if (uid is null)
        {
            return Unauthorized();
        }

        if (isComplete)
        {
            if (expectedVersion == Guid.Empty) return BadRequest();
            await _notebook.CompleteAsync(uid, id, true, expectedVersion, ct);
        }
        else
        {
            if (expectedVersion == Guid.Empty) return BadRequest();
            await _notebook.ReopenAsync(uid, id, expectedVersion, ct);
        }
        return RedirectToCurrent();
    }

    public async Task<IActionResult> OnPostConvertAsync(Guid id, NotebookItemType? newType, CancellationToken ct)
    {
        var uid = _users.GetUserId(User);
        if (uid is null)
        {
            return Unauthorized();
        }

        if (newType is not null)
        {
            var current = await _notebook.GetDetailAsync(uid, id, ct);
            if (current is not null)
            {
                await _notebook.ConvertTypeAsync(uid, id, newType.Value, current.Version, ct);
            }
        }

        return RedirectToCurrent(id);
    }

    public IActionResult OnPostAddLabel(Guid id)
    {
        // SECTION: Open the selected card editor so labels can be managed without JavaScript.
        return RedirectToCurrent(id);
    }

    public async Task<IActionResult> OnPostToggleChecklistItemAsync(
        int checklistItemId,
        bool isDone,
        Guid selectedId,
        Guid expectedVersion,
        CancellationToken ct)
    {
        var uid = _users.GetUserId(User);
        if (uid is null)
        {
            return Unauthorized();
        }

        if (expectedVersion == Guid.Empty) return BadRequest();
        await _notebook.ToggleChecklistItemAsync(uid, selectedId, checklistItemId, isDone, expectedVersion, ct);
        return RedirectToCurrent(selectedId);
    }

    private void PopulateEditorInput()
    {
        if (IsCreateMode())
        {
            Input = new NotebookEditInput { Type = Type ?? NotebookItemType.Note };
            return;
        }

        var selected = Notebook.SelectedItem;
        if (selected is null)
        {
            return;
        }
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
            ChecklistItems = selected.ChecklistItems.Select(x => x.Text).ToArray(),
            ChecklistRows = selected.ChecklistItems.Select(x => new NotebookChecklistEditRow { Id = x.Id, Text = x.Text, IsDone = x.IsDone, SortOrder = x.SortOrder }).ToArray()
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

        // SECTION: Preserve checklist row identity when editable rows are posted.
        Input.ChecklistRows = ReadChecklistRowsFromForm();
    }


    private IReadOnlyList<NotebookChecklistEditRow> ReadChecklistRowsFromForm()
    {
        var rowIndexes = Request.Form.Keys
            .Where(key => key.StartsWith("ChecklistRows[", StringComparison.OrdinalIgnoreCase) && key.EndsWith("].Text", StringComparison.OrdinalIgnoreCase))
            .Select(key => key[14..key.IndexOf(']')])
            .Distinct()
            .ToArray();

        return rowIndexes.Select((index, fallbackOrder) => new NotebookChecklistEditRow
            {
                Id = int.TryParse(Request.Form[$"ChecklistRows[{index}].Id"].ToString(), out var id) ? id : null,
                Text = Request.Form[$"ChecklistRows[{index}].Text"].ToString(),
                IsDone = string.Equals(Request.Form[$"ChecklistRows[{index}].IsDone"].ToString(), "true", StringComparison.OrdinalIgnoreCase),
                SortOrder = int.TryParse(Request.Form[$"ChecklistRows[{index}].SortOrder"].ToString(), out var order) ? order : fallbackOrder
            })
            .Where(row => !string.IsNullOrWhiteSpace(row.Text))
            .ToArray();
    }

    private void NormalizeLegacyTypeView()
    {
        if (string.Equals(View, "sticky", StringComparison.OrdinalIgnoreCase))
        {
            View = "home";
            Filter ??= "sticky";
        }
        else if (string.Equals(View, "notes", StringComparison.OrdinalIgnoreCase))
        {
            View = "home";
            Filter ??= "notes";
        }
        else if (string.Equals(View, "checklists", StringComparison.OrdinalIgnoreCase))
        {
            View = "home";
            Filter ??= "checklists";
        }
    }

    private bool IsCreateMode() => string.Equals(Mode, "new", StringComparison.OrdinalIgnoreCase);

    private IActionResult RedirectToCurrent(Guid? selectedId = null)
    {
        return RedirectToPage(new
        {
            view = View,
            query = Query,
            filter = Filter,
            tag = Tag,
            note = selectedId
        });
    }
}
