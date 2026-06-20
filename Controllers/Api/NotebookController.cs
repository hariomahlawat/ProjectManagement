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

    [HttpGet("~/api/notebook/counts")]
    public async Task<IActionResult> Counts(CancellationToken ct)
    {
        return Ok(await _notebook.GetCountsAsync(CurrentUserId(), ct));
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
        var uid = CurrentUserId();
        var id = await _notebook.CreateAsync(uid, ToInput(request), ct);
        var item = await _notebook.GetDetailAsync(uid, id, ct);
        return CreatedAtAction(nameof(Get), new { id }, await BuildMutationResponseAsync(item!, includeCard: true, ct));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateNotebookItemRequest request, CancellationToken ct)
    {
        var validation = ValidateRequest(request);
        if (validation is not null) return validation;
        if (request.Version == Guid.Empty)
        {
            return BadRequest(new
            {
                code = "notebook_version_required",
                message = "A valid notebook version is required before saving an existing note."
            });
        }

        request.ChecklistRows ??= [];
        request.Labels ??= [];
        var uid = CurrentUserId();
        var updated = await _notebook.UpdateAsync(uid, id, ToInput(request), request.Version, ct);

        return Ok(ToResponse(updated));
    }

    [HttpPost("{id:guid}/pin")]
    public async Task<IActionResult> Pin(Guid id, [FromBody] SetNotebookPinRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var updated = await _notebook.SetPinnedAsync(CurrentUserId(), id, request.IsPinned, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: true, ct));
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, [FromBody] ArchiveNotebookItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var updated = await _notebook.ArchiveAsync(CurrentUserId(), id, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: false, ct));
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteNotebookItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var updated = await _notebook.CompleteAsync(CurrentUserId(), id, true, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: false, ct));
    }

    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id, [FromBody] ReopenNotebookItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var updated = await _notebook.ReopenAsync(CurrentUserId(), id, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: false, ct));
    }


    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromBody] DeleteNotebookItemRequest? request, CancellationToken ct)
    {
        if (request is null || request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var updated = await _notebook.DeleteAsync(CurrentUserId(), id, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: false, ct));
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, [FromBody] RestoreNotebookItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var updated = await _notebook.RestoreAsync(CurrentUserId(), id, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: true, ct));
    }

    [HttpPost("{id:guid}/show-checkboxes")]
    public async Task<IActionResult> ShowCheckboxes(Guid id, [FromBody] ConvertNotebookItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var updated = await _notebook.ConvertTypeAsync(CurrentUserId(), id, NotebookItemType.Checklist, request.Version, ct);
        return Ok(ToResponse(updated));
    }

    [HttpPost("{id:guid}/hide-checkboxes")]
    public async Task<IActionResult> HideCheckboxes(Guid id, [FromBody] ConvertNotebookItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var updated = await _notebook.ConvertTypeAsync(CurrentUserId(), id, NotebookItemType.Note, request.Version, ct);
        return Ok(ToResponse(updated));
    }


    [HttpPatch("{itemId:guid}/checklist-items/{rowId:int}")]
    public async Task<IActionResult> ToggleChecklistItem(Guid itemId, int rowId, [FromBody] ToggleChecklistItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty)
        {
            return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        }

        var item = await _notebook.ToggleChecklistItemAsync(CurrentUserId(), itemId, rowId, request.IsDone, request.Version, ct);
        return Ok(ToResponse(item));
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
        ClientRequestId = request.ClientRequestId == Guid.Empty ? null : request.ClientRequestId,
        Tags = request.Labels ?? [],
        ChecklistRows = request.ChecklistRows ?? []
    };

    private BadRequestObjectResult? ValidateRequest(CreateNotebookItemRequest request)
    {
        request.ChecklistRows ??= [];
        request.Labels ??= [];
        var hasTitle = !string.IsNullOrWhiteSpace(request.Title);
        var hasBody = !string.IsNullOrWhiteSpace(request.Body);
        var rows = request.ChecklistRows.Where(row => !string.IsNullOrWhiteSpace(row.Text)).ToArray();
        if (!Enum.IsDefined(request.Type) || !Enum.IsDefined(request.Priority)) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "type", "Invalid notebook type or priority."));
        if (!hasTitle && !hasBody && rows.Length == 0) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "content", "Add a title, body, or checklist item before saving."));
        if ((request.Title?.Length ?? 0) > NotebookLimits.TitleMaxLength) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "title", $"Title cannot exceed {NotebookLimits.TitleMaxLength} characters."));
        if ((request.Body?.Length ?? 0) > NotebookLimits.BodyMaxLength) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "body", $"Body cannot exceed {NotebookLimits.BodyMaxLength} characters."));
        if (request.ChecklistRows.Count > NotebookLimits.MaxChecklistRows) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "checklistRows", $"Checklist cannot exceed {NotebookLimits.MaxChecklistRows} rows."));
        var oversizedRow = request.ChecklistRows.Select((row, index) => new { row, index }).FirstOrDefault(x => (x.row.Text?.Length ?? 0) > NotebookLimits.ChecklistTextMaxLength);
        if (oversizedRow is not null) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", $"checklistRows[{oversizedRow.index}].text", $"Checklist text cannot exceed {NotebookLimits.ChecklistTextMaxLength} characters."));
        if (request.ChecklistRows.Where(row => row.Id.HasValue).GroupBy(row => row.Id).Any(group => group.Count() > 1)) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "checklistRows", "Duplicate checklist row ids are not allowed."));
        if (request.Labels.Count > NotebookLimits.MaxLabelsPerItem || request.Labels.Any(label => label.Length > NotebookLimits.LabelNameMaxLength)) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "labels", "Too many labels or label text is too long."));
        var allowedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "white", "blue", "amber", "green", "rose", "slate" };
        if (!string.IsNullOrWhiteSpace(request.ColorKey) && !allowedColors.Contains(request.ColorKey)) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "colorKey", "Unsupported colour."));
        return null;
    }


    private async Task<NotebookMutationResponse> BuildMutationResponseAsync(NotebookItemDetailVm item, bool includeCard, CancellationToken ct)
    {
        return new NotebookMutationResponse
        {
            Item = ToResponse(item),
            CardHtml = null,
            Counts = await _notebook.GetCountsAsync(CurrentUserId(), ct)
        };
    }

    private static object ApiError(string code, string message, string field, string error) => new { code, message, errors = new Dictionary<string, string[]> { [field] = new[] { error } } };

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
