using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
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
    private readonly INotebookCardRenderer _cardRenderer;
    private readonly INotebookCardModelFactory _cardModelFactory;
    private readonly IWebHostEnvironment _environment;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<NotebookController> _logger;

    public NotebookController(
        INotebookService notebook,
        INotebookCardRenderer cardRenderer,
        INotebookCardModelFactory cardModelFactory,
        IWebHostEnvironment environment,
        UserManager<ApplicationUser> users,
        ILogger<NotebookController> logger)
    {
        _notebook = notebook;
        _cardRenderer = cardRenderer;
        _cardModelFactory = cardModelFactory;
        _environment = environment;
        _users = users;
        _logger = logger;
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
    public async Task<IActionResult> Card(Guid id, [FromQuery] string view = NotebookCardContexts.Home, CancellationToken ct = default)
    {
        try
        {
            var uid = CurrentUserId();
            var item = await _notebook.GetDetailAsync(uid, id, ct);
            if (item is null)
            {
                return NotFound(new
                {
                    code = "notebook_not_found",
                    message = "The note could not be found."
                });
            }

            var model = _cardModelFactory.Create(item, new NotebookCardContext { View = view });
            var html = await _cardRenderer.RenderAsync(model, ct);
            return Content(html, "text/html; charset=utf-8");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Notebook card endpoint failed for item {ItemId}.", id);

            return _environment.IsDevelopment()
                ? Problem(title: "Notebook card rendering failed", detail: ex.ToString(), statusCode: StatusCodes.Status500InternalServerError)
                : Problem(title: "Notebook card rendering failed", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // SECTION: Item mutation endpoints
    [Consumes("application/json")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateNotebookItemRequest request, CancellationToken ct)
    {
        var validation = ValidateCreateRequest(request);
        if (validation is not null) return validation;
        var uid = CurrentUserId();
        var item = await _notebook.CreateAsync(uid, ToCreateInput(request), ct);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, await BuildMutationResponseAsync(item, includeCard: true, ct));
    }

    [Consumes("application/json")]
    [HttpPatch("{id:guid}")]
    // Legacy aggregate update endpoint.
    // Do not use for autosave.
    // Omitted collections may be interpreted as clears.
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateNotebookItemRequest request, CancellationToken ct)
    {
        var validation = ValidateUpdateRequest(request);
        if (validation is not null) return validation;
        if (request.Version == Guid.Empty)
        {
            return BadRequest(ApiError("notebook_validation_failed", "The note could not be saved.", "version", "Version is required."));
        }

        request.ChecklistRows ??= [];
        request.Labels ??= [];
        var uid = CurrentUserId();
        var updated = await _notebook.UpdateAsync(uid, id, ToUpdateInput(request), request.Version, ct);

        return Ok(await BuildMutationResponseAsync(updated, includeCard: true, ct));
    }


    [Consumes("application/json")]
    [HttpPatch("{id:guid}/content")]
    public async Task<IActionResult> UpdateContent(Guid id, [FromBody] UpdateNotebookContentRequest request, CancellationToken ct)
    {
        // SECTION: Browser autosave endpoint for text-only updates.
        var validation = ValidateContentRequest(request);
        if (validation is not null) return validation;

        var updated = await _notebook.UpdateContentAsync(CurrentUserId(), id, request.Title, request.Body, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: true, ct));
    }

    [Consumes("application/json")]
    [HttpPut("{id:guid}/checklist")]
    public async Task<IActionResult> UpdateChecklist(Guid id, [FromBody] UpdateNotebookChecklistRequest request, CancellationToken ct)
    {
        // SECTION: Browser autosave endpoint for checklist content and rows.
        var validation = ValidateChecklistUpdate(request);
        if (validation is not null) return validation;

        var updated = await _notebook.UpdateChecklistAsync(CurrentUserId(), id, request.Title, request.Body, request.ChecklistRows, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: true, ct));
    }

    [Consumes("application/json")]
    [HttpPost("{id:guid}/pin")]
    public async Task<IActionResult> Pin(Guid id, [FromBody] SetNotebookPinRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var updated = await _notebook.SetPinnedAsync(CurrentUserId(), id, request.IsPinned, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: true, ct));
    }

    [Consumes("application/json")]
    [HttpPut("~/api/notebook/order")]
    public async Task<IActionResult> Reorder([FromBody] ReorderNotebookItemsRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<NotebookBoardSection>(request.Section, ignoreCase: true, out var section))
        {
            return BadRequest(ApiError("notebook_validation_failed", "The notebook order is invalid.", "section", "Section must be Pinned or Others."));
        }

        request.Items ??= [];
        if (request.Items.Any(item => item.Id == Guid.Empty || item.Version == Guid.Empty))
        {
            return BadRequest(ApiError("notebook_validation_failed", "The notebook order is invalid.", "items", "Every item must include a valid id and version."));
        }

        await _notebook.ReorderAsync(
            CurrentUserId(),
            section,
            request.Items.Select(item => new NotebookOrderItem(item.Id, item.Version)).ToArray(),
            ct);

        return Ok(new
        {
            section = section.ToString().ToLowerInvariant(),
            itemIds = request.Items.Select(item => item.Id).ToArray()
        });
    }

    [Consumes("application/json")]
    [HttpPost("{id:guid}/colour")]
    public async Task<IActionResult> SetColour(Guid id, [FromBody] SetNotebookColourRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty)
        {
            return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        }

        var normalisedColour = NormaliseColourKey(request.ColorKey);
        if (request.ColorKey is not null && normalisedColour is null)
        {
            return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "colorKey", "Unsupported colour."));
        }

        var updated = await _notebook.SetColourAsync(CurrentUserId(), id, normalisedColour, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: true, ct));
    }

    [HttpGet("~/api/notebook/labels")]
    public async Task<IActionResult> Labels(CancellationToken ct)
    {
        var labels = await _notebook.GetLabelsAsync(CurrentUserId(), ct);
        return Ok(labels.Select(ToLabelSummary));
    }

    [Consumes("application/json")]
    [HttpPost("~/api/notebook/labels")]
    public async Task<IActionResult> CreateLabel([FromBody] CreateNotebookLabelRequest request, CancellationToken ct)
    {
        var label = await _notebook.CreateLabelAsync(CurrentUserId(), request.Name, ct);
        var labels = await _notebook.GetLabelsAsync(CurrentUserId(), ct);
        return Ok(new NotebookLabelsMutationResponse
        {
            Label = ToLabelSummary(label),
            Labels = labels.Select(ToLabelSummary).ToList()
        });
    }


    [Consumes("application/json")]
    [HttpPost("{id:guid}/labels")]
    public async Task<IActionResult> SetLabels(Guid id, [FromBody] SetNotebookLabelsRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty)
        {
            return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        }
        request.Labels ??= [];
        if (request.Labels.Count > NotebookLimits.MaxLabelsPerItem || request.Labels.Any(x => x.Trim().TrimStart('#').Length > NotebookLimits.LabelNameMaxLength))
        {
            return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "labels", "Too many labels or label text is too long."));
        }
        var updated = await _notebook.SetLabelsAsync(CurrentUserId(), id, request.Labels, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: true, ct));
    }

    [Consumes("application/json")]
    [HttpPatch("~/api/notebook/labels/{labelId:int}")]
    public async Task<IActionResult> RenameLabel(int labelId, [FromBody] RenameNotebookLabelRequest request, CancellationToken ct)
    {
        var result = await _notebook.RenameLabelAsync(CurrentUserId(), labelId, request.Name, ct);
        return Ok(new NotebookLabelsMutationResponse
        {
            Labels = result.Labels.Select(ToLabelSummary).ToList(),
            AffectedItemIds = result.AffectedItemIds.ToList()
        });
    }

    [HttpDelete("~/api/notebook/labels/{labelId:int}")]
    public async Task<IActionResult> DeleteLabel(int labelId, CancellationToken ct)
    {
        var result = await _notebook.DeleteLabelAsync(CurrentUserId(), labelId, ct);
        return Ok(new NotebookLabelsMutationResponse
        {
            Labels = result.Labels.Select(ToLabelSummary).ToList(),
            AffectedItemIds = result.AffectedItemIds.ToList()
        });
    }

    [Consumes("application/json")]
    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, [FromBody] ArchiveNotebookItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var updated = await _notebook.ArchiveAsync(CurrentUserId(), id, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: false, ct));
    }

    [Consumes("application/json")]
    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteNotebookItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var updated = await _notebook.CompleteAsync(CurrentUserId(), id, true, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: false, ct));
    }

    [Consumes("application/json")]
    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id, [FromBody] ReopenNotebookItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var updated = await _notebook.ReopenAsync(CurrentUserId(), id, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: false, ct));
    }


    [Consumes("application/json")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromBody] DeleteNotebookItemRequest? request, CancellationToken ct)
    {
        if (request is null || request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        await _notebook.MoveToTrashAsync(CurrentUserId(), id, request.Version, ct);
        return Ok(await BuildRemovalResponseAsync(id, ct));
    }

    [Consumes("application/json")]
    [HttpPost("{id:guid}/trash")]
    public async Task<IActionResult> MoveToTrash(Guid id, [FromBody] DeleteNotebookItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var trashed = await _notebook.MoveToTrashAsync(CurrentUserId(), id, request.Version, ct);
        return Ok(new NotebookMutationResponse
        {
            Item = ToResponse(trashed),
            RemovedItemId = id,
            Counts = await TryGetCountsAsync(CurrentUserId(), ct)
        });
    }

    [Consumes("application/json")]
    [HttpPost("{id:guid}/restore-from-trash")]
    public async Task<IActionResult> RestoreFromTrash(Guid id, [FromBody] RestoreNotebookTrashItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var updated = await _notebook.RestoreFromTrashAsync(CurrentUserId(), id, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: true, ct));
    }

    [Consumes("application/json")]
    [HttpDelete("{id:guid}/permanent")]
    public async Task<IActionResult> DeletePermanently(Guid id, [FromBody] PermanentlyDeleteNotebookItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        await _notebook.DeletePermanentlyAsync(CurrentUserId(), id, request.Version, ct);
        return Ok(await BuildRemovalResponseAsync(id, ct));
    }

    [HttpDelete("~/api/notebook/trash")]
    public async Task<IActionResult> EmptyTrash(CancellationToken ct)
    {
        var removed = await _notebook.EmptyTrashAsync(CurrentUserId(), ct);
        return Ok(new { removed, counts = await TryGetCountsAsync(CurrentUserId(), ct) });
    }

    [Consumes("application/json")]
    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, [FromBody] RestoreNotebookItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var updated = await _notebook.RestoreAsync(CurrentUserId(), id, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: false, ct));
    }

    [Consumes("application/json")]
    [HttpPost("{id:guid}/show-checkboxes")]
    public async Task<IActionResult> ShowCheckboxes(Guid id, [FromBody] ConvertNotebookItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var updated = await _notebook.ConvertTypeAsync(CurrentUserId(), id, NotebookItemType.Checklist, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: true, ct));
    }

    [Consumes("application/json")]
    [HttpPost("{id:guid}/hide-checkboxes")]
    public async Task<IActionResult> HideCheckboxes(Guid id, [FromBody] ConvertNotebookItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        var updated = await _notebook.ConvertTypeAsync(CurrentUserId(), id, NotebookItemType.Note, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(updated, includeCard: true, ct));
    }


    [Consumes("application/json")]
    [HttpPatch("{itemId:guid}/checklist-items/{rowId:int}")]
    public async Task<IActionResult> ToggleChecklistItem(Guid itemId, int rowId, [FromBody] ToggleChecklistItemRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty)
        {
            return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "version", "A valid notebook version is required."));
        }

        var item = await _notebook.ToggleChecklistItemAsync(CurrentUserId(), itemId, rowId, request.IsDone, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(item, includeCard: true, ct));
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<IActionResult> Duplicate(Guid id, CancellationToken ct)
    {
        var copy = await _notebook.DuplicateAsync(CurrentUserId(), id, ct);
        return Ok(await BuildMutationResponseAsync(copy, includeCard: true, ct));
    }

    // SECTION: Notebook collaboration endpoints
    [HttpGet("{id:guid}/collaborators")]
    public async Task<IActionResult> Collaborators(Guid id, CancellationToken ct)
    {
        var rows = await _notebook.GetCollaboratorsAsync(CurrentUserId(), id, ct);
        return Ok(rows.Select(ToCollaboratorResponse));
    }

    [HttpGet("{id:guid}/collaborator-search")]
    public async Task<IActionResult> SearchCollaborators(Guid id, [FromQuery] string query, CancellationToken ct)
    {
        var rows = await _notebook.SearchCollaboratorsAsync(CurrentUserId(), id, query, 10, ct);
        return Ok(rows.Select(row => new NotebookCollaboratorSearchResponse
        {
            UserId = row.UserId,
            DisplayName = row.DisplayName,
            Email = row.Email,
            Initials = Initials(row.DisplayName)
        }));
    }

    [Consumes("application/json")]
    [HttpPost("{id:guid}/collaborators")]
    public async Task<IActionResult> AddCollaborator(Guid id, [FromBody] AddNotebookCollaboratorRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty || string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(ApiError("notebook_validation_failed", "The collaborator could not be added.", "userId", "Select a valid user."));
        var item = await _notebook.AddCollaboratorAsync(CurrentUserId(), id, request.UserId, request.Role, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(item, includeCard: true, ct));
    }

    [Consumes("application/json")]
    [HttpDelete("{id:guid}/collaborators/{collaboratorUserId}")]
    public async Task<IActionResult> RemoveCollaborator(Guid id, string collaboratorUserId, [FromBody] RemoveNotebookCollaboratorRequest request, CancellationToken ct)
    {
        if (request.Version == Guid.Empty)
            return BadRequest(ApiError("notebook_validation_failed", "The collaborator could not be removed.", "version", "Version is required."));
        var item = await _notebook.RemoveCollaboratorAsync(CurrentUserId(), id, collaboratorUserId, request.Version, ct);
        return Ok(await BuildMutationResponseAsync(item, includeCard: true, ct));
    }

    [HttpPost("{id:guid}/leave")]
    public async Task<IActionResult> LeaveCollaboration(Guid id, CancellationToken ct)
    {
        await _notebook.LeaveCollaborationAsync(CurrentUserId(), id, ct);
        return Ok(await BuildRemovalResponseAsync(id, ct));
    }

    // SECTION: Mapping and validation helpers
    private string CurrentUserId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Authenticated user id is unavailable.");

    private static NotebookCreateInput ToCreateInput(CreateNotebookItemRequest request) => new()
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

    private static NotebookUpdateInput ToUpdateInput(UpdateNotebookItemRequest request) => new()
    {
        Title = request.Title ?? string.Empty,
        BodyMarkdown = request.Body,
        Priority = request.Priority,
        ReminderAtUtc = request.ReminderAtUtc,
        ColorKey = request.ColorKey,
        Tags = request.Labels ?? [],
        ChecklistRows = request.ChecklistRows ?? []
    };

    private BadRequestObjectResult? ValidateCreateRequest(CreateNotebookItemRequest request)
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

    private BadRequestObjectResult? ValidateUpdateRequest(UpdateNotebookItemRequest request)
    {
        request.ChecklistRows ??= [];
        request.Labels ??= [];
        var hasTitle = !string.IsNullOrWhiteSpace(request.Title);
        var hasBody = !string.IsNullOrWhiteSpace(request.Body);
        var rows = request.ChecklistRows.Where(row => !string.IsNullOrWhiteSpace(row.Text)).ToArray();
        if (request.Priority.HasValue && !Enum.IsDefined(request.Priority.Value)) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "priority", "Invalid notebook priority."));
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

    private BadRequestObjectResult? ValidateContentRequest(UpdateNotebookContentRequest request)
    {
        // SECTION: Content autosave request validation.
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The note could not be saved.", "version", "Version is required."));
        if (string.IsNullOrWhiteSpace(request.Title) && string.IsNullOrWhiteSpace(request.Body)) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "content", "Add a title or body before saving."));
        if ((request.Title?.Length ?? 0) > NotebookLimits.TitleMaxLength) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "title", $"Title cannot exceed {NotebookLimits.TitleMaxLength} characters."));
        if ((request.Body?.Length ?? 0) > NotebookLimits.BodyMaxLength) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "body", $"Body cannot exceed {NotebookLimits.BodyMaxLength} characters."));
        return null;
    }

    private BadRequestObjectResult? ValidateChecklistUpdate(UpdateNotebookChecklistRequest request)
    {
        // SECTION: Checklist autosave request validation.
        if (request.Version == Guid.Empty) return BadRequest(ApiError("notebook_validation_failed", "The note could not be saved.", "version", "Version is required."));

        request.Title = request.Title?.Trim();
        request.Body = request.Body?.Trim();
        request.ChecklistRows ??= [];

        if (string.IsNullOrWhiteSpace(request.Title) && string.IsNullOrWhiteSpace(request.Body) && request.ChecklistRows.Count == 0)
        {
            return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "content", "Enter a title, note or at least one checklist item."));
        }

        if ((request.Title?.Length ?? 0) > NotebookLimits.TitleMaxLength) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "title", $"Title cannot exceed {NotebookLimits.TitleMaxLength} characters."));
        if ((request.Body?.Length ?? 0) > NotebookLimits.BodyMaxLength) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "body", $"Body cannot exceed {NotebookLimits.BodyMaxLength} characters."));
        if (request.ChecklistRows.Count > NotebookLimits.MaxChecklistRows) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "checklistRows", $"Checklist cannot exceed {NotebookLimits.MaxChecklistRows} rows."));
        if (request.ChecklistRows.Where(row => row.Id.HasValue).GroupBy(row => row.Id!.Value).Any(group => group.Count() > 1)) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "checklistRows", "Duplicate checklist row ids are not allowed."));
        if (request.ChecklistRows.Where(row => !string.IsNullOrWhiteSpace(row.ClientKey)).GroupBy(row => row.ClientKey!, StringComparer.Ordinal).Any(group => group.Count() > 1)) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", "checklistRows", "The checklist contains duplicate client row identifiers."));

        for (var index = 0; index < request.ChecklistRows.Count; index++)
        {
            var row = request.ChecklistRows[index];
            row.Text = row.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(row.Text)) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", $"checklistRows[{index}].text", "Checklist item text is required."));
            if (row.Text.Length > NotebookLimits.ChecklistTextMaxLength) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", $"checklistRows[{index}].text", $"Checklist text cannot exceed {NotebookLimits.ChecklistTextMaxLength} characters."));
            row.ClientKey = string.IsNullOrWhiteSpace(row.ClientKey) ? null : row.ClientKey.Trim();
            if ((row.ClientKey?.Length ?? 0) > 100) return BadRequest(ApiError("notebook_validation_failed", "The notebook item is invalid.", $"checklistRows[{index}].clientKey", "Client row identifier cannot exceed 100 characters."));
            row.SortOrder = index;
        }

        return null;
    }


    private async Task<NotebookMutationResponse> BuildMutationResponseAsync(NotebookItemDetailVm item, bool includeCard, CancellationToken ct)
    {
        // SECTION: Mutation responses always render canonical Home-board card markup.
        var ownerId = CurrentUserId();
        var cardHtml = includeCard ? await TryRenderCardAsync(item, NotebookCardContexts.Home, ct) : null;

        return new NotebookMutationResponse
        {
            Item = ToResponse(item),
            CardHtml = cardHtml,
            Counts = await TryGetCountsAsync(ownerId, ct)
        };
    }

    // SECTION: Best-effort card rendering for saved mutations
    private async Task<string?> TryRenderCardAsync(NotebookItemDetailVm item, string view, CancellationToken ct)
    {
        try
        {
            var model = _cardModelFactory.Create(ToListItem(item), new NotebookCardContext { View = view });
            return await _cardRenderer.RenderAsync(model, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                """
                Notebook card rendering failed.
                ItemId: {ItemId}
                View: {NotebookView}
                PartialPath: {PartialPath}
                ExceptionType: {ExceptionType}
                InnerException: {InnerException}
                RouteValues: {@RouteValues}
                """,
                item.Id,
                view,
                RazorNotebookCardRenderer.CardPartialPath,
                ex.GetType().FullName,
                ex.InnerException?.ToString(),
                HttpContext.GetRouteData()?.Values);
            return null;
        }
    }

    private async Task<NotebookMutationResponse> BuildRemovalResponseAsync(Guid removedItemId, CancellationToken ct) => new()
    {
        RemovedItemId = removedItemId,
        Counts = await TryGetCountsAsync(CurrentUserId(), ct)
    };

    // SECTION: Best-effort count refresh for saved mutations
    private async Task<NotebookCountsResponse?> TryGetCountsAsync(string ownerId, CancellationToken ct)
    {
        try
        {
            return ToCountsResponse(await _notebook.GetCountsAsync(ownerId, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Notebook mutation succeeded, but counts could not be refreshed for owner {OwnerId}.", ownerId);
            return null;
        }
    }

    private static NotebookCountsResponse ToCountsResponse(IReadOnlyDictionary<string, int> counts) => new()
    {
        Home = counts.GetValueOrDefault("home"),
        Today = counts.GetValueOrDefault("today"),
        Reminders = counts.GetValueOrDefault("reminders"),
        Shared = counts.GetValueOrDefault("shared"),
        Labels = counts.GetValueOrDefault("labels"),
        Archive = counts.GetValueOrDefault("archive", counts.GetValueOrDefault("archived")),
        Completed = counts.GetValueOrDefault("completed"),
        Trash = counts.GetValueOrDefault("trash"),
        Pinned = counts.GetValueOrDefault("pinned"),
        Others = counts.GetValueOrDefault("others")
    };

    private static NotebookItemListVm ToListItem(NotebookItemDetailVm item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        Preview = item.Preview,
        Type = item.Type,
        Status = item.Status,
        Priority = item.Priority,
        ReminderAtUtc = item.ReminderAtUtc,
        ReminderDisplay = item.ReminderDisplay,
        IsPinned = item.IsPinned,
        IsFavorite = item.IsFavorite,
        ColorKey = item.ColorKey,
        UpdatedAtUtc = item.UpdatedAtUtc,
        DeletedAtUtc = item.DeletedAtUtc,
        Tags = item.Tags,
        ChecklistTotal = item.ChecklistTotal,
        ChecklistDone = item.ChecklistDone,
        ChecklistPreviewItems = item.ChecklistPreviewItems,
        IsOverdue = item.IsOverdue,
        IsDueToday = item.IsDueToday,
        Version = item.Version,
        OwnerId = item.OwnerId,
        OwnerDisplayName = item.OwnerDisplayName,
        AccessLevel = item.AccessLevel,
        IsShared = item.IsShared,
        Collaborators = item.Collaborators
    };

    private static string? NormaliseColourKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalised = value.Trim().ToLowerInvariant();
        return normalised is "white" or "blue" or "amber" or "green" or "rose" or "slate"
            ? normalised
            : null;
    }

    private static object ApiError(string code, string message, string field, string error) => new { code, message, errors = new Dictionary<string, string[]> { [field] = new[] { error } } };

    private static NotebookLabelSummaryResponse ToLabelSummary(NotebookTagVm label) => new()
    {
        Id = label.Id,
        Name = label.Name,
        Count = label.Count
    };

    private static NotebookCollaboratorResponse ToCollaboratorResponse(NotebookCollaboratorVm row) => new()
    {
        UserId = row.UserId,
        DisplayName = row.DisplayName,
        Email = row.Email,
        Initials = string.IsNullOrWhiteSpace(row.Initials) ? Initials(row.DisplayName) : row.Initials,
        Role = row.Role,
        IsOwner = row.IsOwner
    };

    private static string Initials(string value)
    {
        var parts = (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
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
        DeletedAtUtc = item.DeletedAtUtc,
        Version = item.Version,
        OwnerId = item.OwnerId,
        OwnerDisplayName = item.OwnerDisplayName,
        AccessLevel = item.AccessLevel.ToString(),
        IsShared = item.IsShared,
        Collaborators = item.Collaborators.Select(ToCollaboratorResponse).ToList(),
        ChecklistRows = item.ChecklistItems.Select(row => new NotebookChecklistRowResponse { Id = row.Id, ClientKey = row.ClientKey, Text = row.Text, IsDone = row.IsDone, SortOrder = row.SortOrder }).ToList(),
        Labels = item.Tags.Select(tag => new NotebookLabelResponse { Name = tag }).ToList()
    };
}
