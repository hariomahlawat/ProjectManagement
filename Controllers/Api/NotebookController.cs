using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Contracts.Notebook;
using ProjectManagement.Models;
using ProjectManagement.Services.Notebook;
using ProjectManagement.ViewModels.Notebook;

namespace ProjectManagement.Controllers.Api;

[Authorize]
[ApiController]
[AutoValidateAntiforgeryToken]
[TypeFilter(typeof(NotebookApiExceptionFilter))]
[Route("api/notebook/items")]
public sealed class NotebookController : Controller
{
    private readonly INotebookService _notebook;
    private readonly UserManager<ApplicationUser> _users;

    public NotebookController(INotebookService notebook, UserManager<ApplicationUser> users)
    {
        _notebook = notebook;
        _users = users;
    }

    // SECTION: Item query endpoints
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var uid = CurrentUserId();
        var item = await _notebook.GetDetailAsync(uid, id, ct);
        return item is null ? NotFound() : Ok(ToResponse(item));
    }

    [HttpGet("{id:guid}/card")]
    public async Task<IActionResult> Card(Guid id, [FromQuery] string view = "home", CancellationToken ct = default)
    {
        var uid = CurrentUserId();
        var item = await _notebook.GetDetailAsync(uid, id, ct);
        if (item is null) return NotFound();
        return PartialView("~/Pages/Notebook/_NotebookCard.cshtml", new NotebookCardRenderVm { Item = item, View = view });
    }

    // SECTION: Item mutation endpoints
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateNotebookItemRequest request, CancellationToken ct)
    {
        var validation = ValidateRequest(request);
        if (validation is not null) return validation;
        var id = await _notebook.CreateAsync(CurrentUserId(), ToInput(request), ct);
        var item = await _notebook.GetDetailAsync(CurrentUserId(), id, ct);
        return CreatedAtAction(nameof(Get), new { id }, ToResponse(item!));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateNotebookItemRequest request, CancellationToken ct)
    {
        var validation = ValidateRequest(request);
        if (validation is not null) return validation;
        if (!Guid.TryParse(request.Version, out var parsedVersion) || parsedVersion == Guid.Empty)
        {
            return BadRequest(new
            {
                code = "notebook_version_required",
                message = "A valid notebook version is required before saving an existing note."
            });
        }

        var uid = CurrentUserId();
        await _notebook.UpdateAsync(uid, id, ToInput(request), parsedVersion.ToString("N"), ct);

        return Ok(ToResponse((await _notebook.GetDetailAsync(uid, id, ct))!));
    }

    [HttpPost("{id:guid}/pin")]
    public async Task<IActionResult> Pin(Guid id, [FromBody] SetNotebookPinRequest request, CancellationToken ct)
    {
        await _notebook.SetPinnedAsync(CurrentUserId(), id, request.IsPinned, ct);
        return Ok(ToResponse((await _notebook.GetDetailAsync(CurrentUserId(), id, ct))!));
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct) { await _notebook.ArchiveAsync(CurrentUserId(), id, ct); return NoContent(); }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct) { await _notebook.CompleteAsync(CurrentUserId(), id, true, ct); return NoContent(); }

    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken ct) { await _notebook.ReopenAsync(CurrentUserId(), id, ct); return NoContent(); }


    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _notebook.DeleteAsync(CurrentUserId(), id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        await _notebook.RestoreAsync(CurrentUserId(), id, ct);
        return Ok(ToResponse((await _notebook.GetDetailAsync(CurrentUserId(), id, ct))!));
    }

    [HttpPost("{id:guid}/show-checkboxes")]
    public async Task<IActionResult> ShowCheckboxes(Guid id, CancellationToken ct)
    {
        await _notebook.ConvertTypeAsync(CurrentUserId(), id, NotebookItemType.Checklist, ct);
        return Ok(ToResponse((await _notebook.GetDetailAsync(CurrentUserId(), id, ct))!));
    }

    [HttpPost("{id:guid}/hide-checkboxes")]
    public async Task<IActionResult> HideCheckboxes(Guid id, CancellationToken ct)
    {
        await _notebook.ConvertTypeAsync(CurrentUserId(), id, NotebookItemType.Note, ct);
        return Ok(ToResponse((await _notebook.GetDetailAsync(CurrentUserId(), id, ct))!));
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<IActionResult> Duplicate(Guid id, CancellationToken ct)
    {
        var copyId = await _notebook.DuplicateAsync(CurrentUserId(), id, ct);
        return Ok(ToResponse((await _notebook.GetDetailAsync(CurrentUserId(), copyId, ct))!));
    }

    // SECTION: Mapping and validation helpers
    private string CurrentUserId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Authenticated user id is unavailable.");

    private static NotebookEditInput ToInput(CreateNotebookItemRequest request) => new()
    {
        Title = request.Title ?? string.Empty,
        BodyMarkdown = request.Body,
        Type = request.Type,
        Priority = request.Priority,
        ReminderAtUtc = request.ReminderAtUtc,
        ColorKey = request.ColorKey,
        IsPinned = request.IsPinned,
        Tags = request.Labels,
        ChecklistRows = request.ChecklistRows
    };

    private BadRequestObjectResult? ValidateRequest(CreateNotebookItemRequest request)
    {
        var hasTitle = !string.IsNullOrWhiteSpace(request.Title);
        var hasBody = !string.IsNullOrWhiteSpace(request.Body);
        var rows = request.ChecklistRows.Where(row => !string.IsNullOrWhiteSpace(row.Text)).ToArray();
        if (!Enum.IsDefined(request.Type) || !Enum.IsDefined(request.Priority)) return BadRequest(new { error = "Invalid notebook type or priority." });
        if (!hasTitle && !hasBody && rows.Length == 0) return BadRequest(new { message = "Add a title, body, or checklist item before saving." });
        if ((request.Title?.Length ?? 0) > NotebookLimits.TitleMaxLength || (request.Body?.Length ?? 0) > NotebookLimits.BodyMaxLength) return BadRequest(new { message = "Notebook title or body is too long." });
        if (request.ChecklistRows.Count > NotebookLimits.MaxChecklistRows) return BadRequest(new { error = "Too many checklist rows." });
        if (request.ChecklistRows.Where(row => row.Id.HasValue).GroupBy(row => row.Id).Any(group => group.Count() > 1)) return BadRequest(new { error = "Duplicate checklist row ids are not allowed." });
        if (request.Labels.Count > NotebookLimits.MaxLabelsPerItem || request.Labels.Any(label => label.Length > NotebookLimits.LabelNameMaxLength)) return BadRequest(new { error = "Too many labels or label text is too long." });
        var allowedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "white", "blue", "amber", "green", "rose", "slate" };
        if (!string.IsNullOrWhiteSpace(request.ColorKey) && !allowedColors.Contains(request.ColorKey)) return BadRequest(new { error = "Unsupported colour." });
        return null;
    }

    private static NotebookItemResponse ToResponse(NotebookItemDetailVm item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        Body = item.BodyMarkdown,
        Type = item.Type,
        Status = item.Status,
        Priority = item.Priority,
        IsPinned = item.IsPinned,
        ColorKey = item.ColorKey,
        ReminderAtUtc = item.ReminderAtUtc,
        ReminderDisplay = item.ReminderDisplay,
        UpdatedAtUtc = item.UpdatedAtUtc,
        Version = item.Version,
        ChecklistRows = item.ChecklistItems.Select(row => new NotebookChecklistRowResponse { Id = row.Id, Text = row.Text, IsDone = row.IsDone, SortOrder = row.SortOrder }).ToList(),
        Labels = item.Tags.Select(tag => new NotebookLabelResponse { Name = tag }).ToList()
    };
}
