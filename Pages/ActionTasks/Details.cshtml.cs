using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Pages.ActionTasks;

[Authorize(Policy = "ActionTracker.Access")]
public sealed class DetailsModel : PageModel
{
    private readonly IActionTaskService _service;
    private readonly IActionTaskCollaborationService _collaboration;
    private readonly ActionTaskPermissionService _permission;
    private readonly ActionTaskWorkflowPolicy _workflow;
    private readonly ActionTaskInspectorReadModelBuilder _detailsBuilder;
    private readonly ActionTaskUserLookupService _userLookup;
    private readonly ActionSprintService _sprintService;
    private readonly IActionTrackerClock _clock;
    private readonly UserManager<ApplicationUser> _users;

    public DetailsModel(
        IActionTaskService service,
        IActionTaskCollaborationService collaboration,
        ActionTaskPermissionService permission,
        ActionTaskWorkflowPolicy workflow,
        ActionTaskInspectorReadModelBuilder detailsBuilder,
        ActionTaskUserLookupService userLookup,
        ActionSprintService sprintService,
        IActionTrackerClock clock,
        UserManager<ApplicationUser> users)
    {
        _service = service;
        _collaboration = collaboration;
        _permission = permission;
        _workflow = workflow;
        _detailsBuilder = detailsBuilder;
        _userLookup = userLookup;
        _sprintService = sprintService;
        _clock = clock;
        _users = users;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Intent { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string BackUrl { get; private set; } = string.Empty;

    [BindProperty]
    public TaskRemarkInput RemarkInput { get; set; } = new();

    [BindProperty]
    public ChangeDateInputModel ChangeDateInput { get; set; } = new();

    [BindProperty]
    public TaskEditInput EditTaskInput { get; set; } = new();

    [BindProperty]
    public TaskReassignInput ReassignInput { get; set; } = new();

    [BindProperty]
    public TaskPriorityInput PriorityInput { get; set; } = new();

    public ActionTaskItem TaskItem { get; private set; } = default!;
    public IReadOnlyList<ActionTaskUpdate> Updates { get; private set; } = Array.Empty<ActionTaskUpdate>();
    public IReadOnlyList<ActionTaskAuditLog> Logs { get; private set; } = Array.Empty<ActionTaskAuditLog>();
    public IReadOnlyDictionary<int, IReadOnlyList<ActionTaskAttachmentMetadata>> Attachments { get; private set; } = new Dictionary<int, IReadOnlyList<ActionTaskAttachmentMetadata>>();
    public IReadOnlyDictionary<string, string> ActorNames { get; private set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> AssigneeNames { get; private set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<ActionSprint> Sprints { get; private set; } = Array.Empty<ActionSprint>();
    public IReadOnlyList<UserOption> AssignableUsers { get; private set; } = Array.Empty<UserOption>();
    public DateTime? LastActivityAtUtc { get; private set; }
    public string CurrentUserId { get; private set; } = string.Empty;
    public string CurrentRole { get; private set; } = string.Empty;
    public DateTime IstToday => _clock.IstToday;
    public string DefaultCollectionView => _permission.CanViewAll(CurrentRole) ? "CommandCentre" : "MyWork";

    public bool IsBacklog => ActionTaskCategorization.IsBacklogTask(TaskItem);
    public bool IsClosed => IsStatus(ActionTaskStatuses.Closed);
    public bool IsSubmitted => IsStatus(ActionTaskStatuses.Submitted);
    public bool IsAssigned => IsStatus(ActionTaskStatuses.Assigned);
    public bool IsInProgress => IsStatus(ActionTaskStatuses.InProgress);
    public bool IsBlocked => IsStatus(ActionTaskStatuses.Blocked);
    public ActionTaskInteractionCapabilities Capabilities =>
        TaskItem is null
            ? ActionTaskInteractionCapabilities.None
            : _workflow.GetInteractionCapabilities(TaskItem, CurrentRole, CurrentUserId);
    public bool CanAddRemark => !IsClosed && _permission.CanAddTaskUpdate(CurrentRole, CurrentUserId, TaskItem.AssignedToUserId);
    public bool CanAddConference => !IsClosed && _permission.CanAddConferenceUpdate(CurrentRole);
    public bool CanEditRemark(ActionTaskUpdate update)
        => !IsClosed && _permission.CanMutateTaskRemark(update, CurrentRole, CurrentUserId, _clock.UtcNow);
    public bool CanDeleteRemark(ActionTaskUpdate update)
        => CanEditRemark(update);
    public bool CanUpdateStatus => _workflow.CanUpdateTaskStatus(TaskItem, CurrentRole, CurrentUserId);
    public bool CanSubmit => IsInProgress && _workflow.CanSubmitTask(TaskItem, CurrentUserId);
    public bool CanCommandClose => _permission.CanCloseTaskDirectly(TaskItem, CurrentRole);
    public bool CanReturnForAction => IsSubmitted && _workflow.CanReturnTaskForAction(TaskItem, CurrentRole);
    public bool CanChangeDate => _workflow.CanChangeTaskDate(TaskItem, CurrentRole);
    public bool CanManagePlanning => Capabilities.CanManagePlanning;
    public bool CanViewSystemHistory => _workflow.CanViewSystemHistory(CurrentRole);
    public IReadOnlyList<string> PriorityOptions => _workflow.PriorityOptions;
    public IReadOnlyList<ActionSprint> AssignableSprints => Sprints.Where(sprint => sprint.Status != ActionSprintStatus.Closed).ToList();
    public bool CanRemoveFromSprint => Capabilities.CanRemoveFromSprint
        && TaskItem.SprintId.HasValue
        && Sprints.Any(sprint => sprint.Id == TaskItem.SprintId.Value && sprint.Status != ActionSprintStatus.Closed);
    public bool CanMoveToBacklog => Capabilities.CanMoveToBacklog
        && (!TaskItem.SprintId.HasValue || CanRemoveFromSprint);
    public string DefaultRemarkType => string.Equals(CurrentRole, RoleNames.Comdt, StringComparison.OrdinalIgnoreCase) && CanAddConference
        ? ActionTaskUpdateTypes.Conference
        : ActionTaskUpdateTypes.Comment;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Id = id;
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        RemarkInput.TaskId = id;
        RestoreRemarkDraft(id);
        var rowVersion = Convert.ToBase64String(TaskItem.RowVersion);
        ChangeDateInput.TaskId = id;
        ChangeDateInput.RowVersion = rowVersion;
        ChangeDateInput.NewDate = TaskItem.DueDate.Date >= IstToday ? TaskItem.DueDate.Date : IstToday;

        EditTaskInput.TaskId = id;
        EditTaskInput.RowVersion = rowVersion;
        EditTaskInput.Title = TaskItem.Title;
        EditTaskInput.Description = TaskItem.Description;

        ReassignInput.TaskId = id;
        ReassignInput.RowVersion = rowVersion;
        ReassignInput.AssignedToUserId = TaskItem.AssignedToUserId;

        PriorityInput.TaskId = id;
        PriorityInput.RowVersion = rowVersion;
        PriorityInput.Priority = TaskItem.Priority;

        BackUrl = ResolveBackUrl();
        return Page();
    }

    public async Task<IActionResult> OnPostAddRemarkAsync()
    {
        await ResolveIdentityAsync();
        Id = RemarkInput.TaskId;
        var task = await _service.GetTaskAsync(Id);
        if (task is null)
        {
            return NotFound();
        }

        if (!_permission.CanAddTaskUpdate(CurrentRole, CurrentUserId, task.AssignedToUserId))
        {
            return Forbid();
        }

        ModelState.Clear();
        TryValidateModel(RemarkInput, nameof(RemarkInput));
        if (!ModelState.IsValid)
        {
            PreserveRemarkDraft(RemarkInput);
            TempData["ToastError"] = "Unable to save remark. Please check the entered details and try again.";
            return RedirectToPage(new { id = Id, intent = "remark", returnUrl = SafeReturnUrl() });
        }

        var normalizedType = ActionTaskUpdateTypes.All.FirstOrDefault(type => string.Equals(type, RemarkInput.UpdateType, StringComparison.OrdinalIgnoreCase));
        if (normalizedType is null || string.Equals(normalizedType, ActionTaskUpdateTypes.Progress, StringComparison.OrdinalIgnoreCase))
        {
            TempData["ToastError"] = "Invalid remark type.";
            return RedirectToPage(new { id = Id, intent = "remark", returnUrl = SafeReturnUrl() });
        }

        if (string.Equals(normalizedType, ActionTaskUpdateTypes.Conference, StringComparison.OrdinalIgnoreCase)
            && !_permission.CanAddConferenceUpdate(CurrentRole))
        {
            return Forbid();
        }

        var hasBody = !string.IsNullOrWhiteSpace(RemarkInput.Body);
        var hasFiles = RemarkInput.Files?.Any(file => file.Length > 0) == true;
        if (string.Equals(normalizedType, ActionTaskUpdateTypes.Conference, StringComparison.OrdinalIgnoreCase) && !hasBody)
        {
            PreserveRemarkDraft(RemarkInput);
            TempData["ToastError"] = "Enter the conference direction or observation.";
            return RedirectToPage(new { id = Id, intent = "remark", returnUrl = SafeReturnUrl() });
        }

        if (!hasBody && !hasFiles)
        {
            PreserveRemarkDraft(RemarkInput);
            TempData["ToastError"] = "Enter a remark or attach at least one file.";
            return RedirectToPage(new { id = Id, intent = "remark", returnUrl = SafeReturnUrl() });
        }

        try
        {
            await _collaboration.AddUpdateAsync(Id, RemarkInput.Body, normalizedType, CurrentUserId, CurrentRole, RemarkInput.Files ?? new List<IFormFile>());
            TempData["ToastMessage"] = string.Equals(normalizedType, ActionTaskUpdateTypes.Conference, StringComparison.OrdinalIgnoreCase)
                ? "Conference direction added."
                : "Remark added.";
        }
        catch (InvalidOperationException ex)
        {
            PreserveRemarkDraft(RemarkInput);
            TempData["ToastError"] = ex.Message;
            return RedirectToPage(new { id = Id, intent = "remark", returnUrl = SafeReturnUrl() });
        }

        return RedirectToPage(new { id = Id, returnUrl = SafeReturnUrl() });
    }

    public async Task<IActionResult> OnPostUpdateStatusAsync(int id, string rowVersion, string status, string? remarks)
    {
        await ResolveIdentityAsync();
        var reopenIntent = (string?)null;
        try
        {
            await _service.UpdateStatusAsync(id, DecodeRowVersion(rowVersion), status, CurrentUserId, CurrentRole, remarks);
            TempData["ToastMessage"] = "Task status updated.";
        }
        catch (ActionTaskConcurrencyException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = string.Equals(status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase) ? "block" : null;
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = string.Equals(status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase) ? "block" : null;
        }

        return RedirectToPage(new { id, intent = reopenIntent, returnUrl = SafeReturnUrl() });
    }

    public async Task<IActionResult> OnPostReturnForActionAsync(int id, string rowVersion, string? remarks)
    {
        await ResolveIdentityAsync();
        var reopenIntent = (string?)null;
        try
        {
            await _service.ReturnTaskForActionAsync(id, DecodeRowVersion(rowVersion), remarks ?? string.Empty, CurrentUserId, CurrentRole);
            TempData["ToastMessage"] = "Task returned for further action.";
        }
        catch (ActionTaskConcurrencyException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "return";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "return";
        }

        return RedirectToPage(new { id, intent = reopenIntent, returnUrl = SafeReturnUrl() });
    }

    public async Task<IActionResult> OnPostSubmitAsync(int id, string rowVersion, string? remarks)
    {
        await ResolveIdentityAsync();
        var reopenIntent = (string?)null;
        try
        {
            await _service.SubmitTaskAsync(id, DecodeRowVersion(rowVersion), CurrentUserId, CurrentRole, remarks);
            TempData["ToastMessage"] = "Task submitted for closure.";
        }
        catch (ActionTaskConcurrencyException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "submit";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "submit";
        }

        return RedirectToPage(new { id, intent = reopenIntent, returnUrl = SafeReturnUrl() });
    }

    public async Task<IActionResult> OnPostCloseAsync(int id, string rowVersion, string? remarks, string? closeMode)
    {
        await ResolveIdentityAsync();
        var closeIntent = string.Equals(closeMode, "accept", StringComparison.OrdinalIgnoreCase)
            ? "accept-close"
            : "close-direct";
        var reopenIntent = (string?)null;
        try
        {
            if (closeIntent == "accept-close")
            {
                await _service.AcceptSubmittedTaskAsync(id, DecodeRowVersion(rowVersion), remarks ?? string.Empty, CurrentUserId, CurrentRole);
                TempData["ToastMessage"] = "Task accepted and closed.";
            }
            else
            {
                await _service.CloseTaskDirectlyAsync(id, DecodeRowVersion(rowVersion), remarks ?? string.Empty, CurrentUserId, CurrentRole);
                TempData["ToastMessage"] = "Task closed successfully.";
            }
        }
        catch (ActionTaskConcurrencyException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = closeIntent;
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = closeIntent;
        }

        return RedirectToPage(new { id, intent = reopenIntent, returnUrl = SafeReturnUrl() });
    }

    public async Task<IActionResult> OnPostChangeDateAsync()
    {
        await ResolveIdentityAsync();
        var reopenIntent = (string?)null;
        try
        {
            await _service.UpdateTaskDateAsync(
                ChangeDateInput.TaskId,
                DecodeRowVersion(ChangeDateInput.RowVersion),
                ChangeDateInput.NewDate,
                CurrentUserId,
                CurrentRole,
                ChangeDateInput.Remarks);
            TempData["ToastMessage"] = "Task date updated.";
        }
        catch (ActionTaskConcurrencyException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "change-date";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "change-date";
        }

        return RedirectToPage(new { id = ChangeDateInput.TaskId, intent = reopenIntent, returnUrl = SafeReturnUrl() });
    }

    // SECTION: Correct task title/brief without mixing metadata maintenance into workflow state.
    public async Task<IActionResult> OnPostEditTaskAsync()
    {
        await ResolveIdentityAsync();
        var reopenIntent = (string?)null;

        ModelState.Clear();
        TryValidateModel(EditTaskInput, nameof(EditTaskInput));
        if (!ModelState.IsValid)
        {
            TempData["ToastError"] = "Enter a task title and brief within the allowed limits.";
            return RedirectToPage(new { id = EditTaskInput.TaskId, intent = "edit-task", returnUrl = SafeReturnUrl() });
        }

        try
        {
            await _service.UpdateTaskDetailsAsync(
                EditTaskInput.TaskId,
                DecodeRowVersion(EditTaskInput.RowVersion),
                EditTaskInput.Title,
                EditTaskInput.Description,
                CurrentUserId,
                CurrentRole);
            TempData["ToastMessage"] = "Task details updated.";
        }
        catch (ActionTaskConcurrencyException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "edit-task";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "edit-task";
        }

        return RedirectToPage(new { id = EditTaskInput.TaskId, intent = reopenIntent, returnUrl = SafeReturnUrl() });
    }

    // SECTION: Reassign responsibility directly while preserving the task's workflow state.
    public async Task<IActionResult> OnPostReassignAsync()
    {
        await ResolveIdentityAsync();
        var reopenIntent = (string?)null;

        ModelState.Clear();
        TryValidateModel(ReassignInput, nameof(ReassignInput));
        if (!ModelState.IsValid)
        {
            TempData["ToastError"] = "Select a responsible person and enter a reassignment reason.";
            return RedirectToPage(new { id = ReassignInput.TaskId, intent = "reassign", returnUrl = SafeReturnUrl() });
        }

        try
        {
            var assignedRole = await ResolveAssignableRoleForUserAsync(ReassignInput.AssignedToUserId);
            if (assignedRole is null)
            {
                throw new InvalidOperationException("Select an active responsible person who you are authorised to assign.");
            }

            await _service.ReassignTaskAsync(
                ReassignInput.TaskId,
                DecodeRowVersion(ReassignInput.RowVersion),
                ReassignInput.AssignedToUserId,
                assignedRole,
                ReassignInput.Remarks,
                CurrentUserId,
                CurrentRole);
            TempData["ToastMessage"] = "Task reassigned.";
        }
        catch (ActionTaskConcurrencyException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "reassign";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "reassign";
        }

        return RedirectToPage(new { id = ReassignInput.TaskId, intent = reopenIntent, returnUrl = SafeReturnUrl() });
    }

    // SECTION: Change operational priority without forcing task replanning.
    public async Task<IActionResult> OnPostChangePriorityAsync()
    {
        await ResolveIdentityAsync();
        var reopenIntent = (string?)null;

        ModelState.Clear();
        TryValidateModel(PriorityInput, nameof(PriorityInput));
        if (!ModelState.IsValid)
        {
            TempData["ToastError"] = "Select a priority and enter a short reason.";
            return RedirectToPage(new { id = PriorityInput.TaskId, intent = "priority", returnUrl = SafeReturnUrl() });
        }

        try
        {
            await _service.UpdateTaskPriorityAsync(
                PriorityInput.TaskId,
                DecodeRowVersion(PriorityInput.RowVersion),
                PriorityInput.Priority,
                PriorityInput.Remarks,
                CurrentUserId,
                CurrentRole);
            TempData["ToastMessage"] = "Task priority updated.";
        }
        catch (ActionTaskConcurrencyException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "priority";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "priority";
        }

        return RedirectToPage(new { id = PriorityInput.TaskId, intent = reopenIntent, returnUrl = SafeReturnUrl() });
    }

    // SECTION: Human remarks may be corrected under governance; workflow-generated progress remains immutable.
    public async Task<IActionResult> OnPostEditRemarkAsync(int id, int updateId, string body)
    {
        await ResolveIdentityAsync();
        try
        {
            await _collaboration.EditRemarkAsync(id, updateId, body, CurrentUserId, CurrentRole);
            TempData["ToastMessage"] = "Remark updated.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        return RedirectToPage(new { id, returnUrl = SafeReturnUrl() });
    }

    public async Task<IActionResult> OnPostDeleteRemarkAsync(int id, int updateId)
    {
        await ResolveIdentityAsync();
        try
        {
            await _collaboration.DeleteRemarkAsync(id, updateId, CurrentUserId, CurrentRole);
            TempData["ToastMessage"] = "Remark deleted.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        return RedirectToPage(new { id, returnUrl = SafeReturnUrl() });
    }

    public async Task<IActionResult> OnPostAssignBacklogToSprintAsync(int id, int sprintId, string responsibleUserId)
    {
        await ResolveIdentityAsync();
        var reopenIntent = (string?)null;
        try
        {
            // SECTION: Resolve the responsible person's task role server-side. Never trust a client-supplied role snapshot.
            var assignableUsers = await _userLookup.LoadAssignableUsersAsync(CurrentRole);
            var responsible = assignableUsers.FirstOrDefault(user =>
                string.Equals(user.UserId, responsibleUserId, StringComparison.Ordinal));
            if (responsible is null)
            {
                throw new InvalidOperationException("Select an active responsible person who you are authorised to assign.");
            }

            await _sprintService.AssignBacklogItemToSprintAsync(
                id,
                sprintId,
                responsible.UserId,
                responsible.Role,
                CurrentUserId,
                CurrentRole);
            TempData["ToastMessage"] = "Backlog item assigned to sprint.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "assign-sprint";
        }

        return RedirectToPage(new { id, intent = reopenIntent, returnUrl = SafeReturnUrl() });
    }

    public async Task<IActionResult> OnPostAssignOutsideToSprintAsync(int id, int sprintId)
    {
        await ResolveIdentityAsync();
        var reopenIntent = (string?)null;
        try
        {
            await _sprintService.AssignOutsideSprintTaskToSprintAsync(id, sprintId, CurrentUserId, CurrentRole);
            TempData["ToastMessage"] = "Task added to sprint.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "add-sprint";
        }

        return RedirectToPage(new { id, intent = reopenIntent, returnUrl = SafeReturnUrl() });
    }

    public async Task<IActionResult> OnPostRemoveFromSprintAsync(int id, string? remarks)
    {
        await ResolveIdentityAsync();
        var reopenIntent = (string?)null;
        try
        {
            await _sprintService.RemoveTaskFromSprintKeepAssignedAsync(id, CurrentUserId, CurrentRole, remarks);
            TempData["ToastMessage"] = "Task removed from sprint and kept assigned.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "remove-sprint";
        }

        return RedirectToPage(new { id, intent = reopenIntent, returnUrl = SafeReturnUrl() });
    }

    public async Task<IActionResult> OnPostMoveToBacklogAsync(int id, string? remarks)
    {
        await ResolveIdentityAsync();
        var reopenIntent = (string?)null;
        try
        {
            await _sprintService.MoveTaskToBacklogRemoveAssigneeAsync(id, CurrentUserId, CurrentRole, remarks);
            TempData["ToastMessage"] = "Task moved to backlog.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "backlog";
        }

        return RedirectToPage(new { id, intent = reopenIntent, returnUrl = SafeReturnUrl() });
    }

    public string ResolveActorName(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return "System";
        return ActorNames.TryGetValue(userId, out var name) ? name : userId;
    }

    public string ResolveAssigneeName()
        => AssigneeNames.TryGetValue(TaskItem.AssignedToUserId, out var name) ? name : TaskItem.AssignedToUserId;

    public string ResolveClosedByName()
        => string.IsNullOrWhiteSpace(TaskItem.ClosedByUserId) ? "Not recorded" : ResolveActorName(TaskItem.ClosedByUserId);

    public string GetStatusBadgeClass() => _workflow.GetStatusBadgeClass(TaskItem.Status);
    public string GetPriorityBadgeClass() => _workflow.GetPriorityBadgeClass(TaskItem.Priority);

    public string GetSprintText()
    {
        if (IsBacklog) return "Backlog";
        if (!TaskItem.SprintId.HasValue) return "Assigned, not in sprint";
        return Sprints.FirstOrDefault(s => s.Id == TaskItem.SprintId.Value)?.Name ?? $"Sprint {TaskItem.SprintId.Value}";
    }

    public static string DisplayUpdateType(string? updateType)
    {
        if (string.Equals(updateType, ActionTaskUpdateTypes.Conference, StringComparison.OrdinalIgnoreCase)) return "Conference";
        if (string.Equals(updateType, ActionTaskUpdateTypes.Comment, StringComparison.OrdinalIgnoreCase)) return "General";
        return "Progress";
    }

    public static string DisplayUpdateRole(string? role)
    {
        if (string.Equals(role, RoleNames.Comdt, StringComparison.OrdinalIgnoreCase)) return "Comdt";
        if (string.Equals(role, RoleNames.HoD, StringComparison.OrdinalIgnoreCase)) return "HoD";
        return role?.Trim() ?? string.Empty;
    }

    public string DisplayAuditActionLabel(string actionType)
        => actionType switch
        {
            "TaskCreated" => "Task created",
            "BacklogItemCreated" => "Backlog item created",
            "StatusUpdated" => "Status changed",
            "Submitted" => "Submitted for closure",
            "ReturnedForAction" => "Returned for action",
            "TaskClosedByCommandAuthority" => "Task closed",
            "DueDateChanged" => "Due date changed",
            "TargetDateChanged" => "Target date changed",
            "TaskAssignedToSprint" => "Added to sprint",
            "OutsideSprintTaskAssignedToSprint" => "Added to sprint",
            "TaskRemovedFromSprintKeepAssigned" => "Removed from sprint",
            "TaskMovedToBacklogRemoveAssignee" => "Moved to backlog",
            _ => actionType
        };

    private async Task<bool> LoadAsync(int id)
    {
        await ResolveIdentityAsync();
        var model = await _detailsBuilder.BuildAsync(id, CurrentUserId, CurrentRole);
        if (model.IsUnavailable || model.SelectedTask is null)
        {
            return false;
        }

        TaskItem = model.SelectedTask;
        Updates = model.Updates;
        Logs = model.Logs;
        Attachments = model.UpdateAttachments;
        ActorNames = model.ActorNames;
        LastActivityAtUtc = model.LastActivityAtUtc;
        AssigneeNames = await _userLookup.LoadTaskAssigneeNamesAsync(new[] { TaskItem });
        Sprints = await _sprintService.GetSprintsAsync();
        if (CanManagePlanning || Capabilities.CanReassignTask)
        {
            AssignableUsers = await _userLookup.LoadAssignableUsersAsync(CurrentRole);
        }

        return true;
    }

    private async Task<string?> ResolveAssignableRoleForUserAsync(string assignedToUserId)
    {
        if (string.IsNullOrWhiteSpace(assignedToUserId))
        {
            return null;
        }

        var assignedUser = await _users.FindByIdAsync(assignedToUserId);
        if (assignedUser is null || assignedUser.IsDisabled || assignedUser.PendingDeletion)
        {
            return null;
        }

        if (assignedUser.LockoutEnd.HasValue && assignedUser.LockoutEnd > new DateTimeOffset(_clock.UtcNow, TimeSpan.Zero))
        {
            return null;
        }

        var assignedRoles = await _users.GetRolesAsync(assignedUser);
        var assignedRole = ActionTaskRoleResolver.ResolveAssignableRoleFromRoles(assignedRoles);
        return assignedRole is not null && _permission.CanAssign(CurrentRole, assignedRole)
            ? assignedRole
            : null;
    }

    private async Task ResolveIdentityAsync()
    {
        CurrentUserId = _users.GetUserId(User) ?? string.Empty;
        CurrentRole = ActionTaskRoleResolver.Resolve(User) ?? string.Empty;
        await Task.CompletedTask;
    }

    // SECTION: Preserve collection context when a task is opened from a board/list, while rejecting external redirects.
    private string ResolveBackUrl()
    {
        var safe = SafeReturnUrl();
        if (!string.IsNullOrWhiteSpace(safe))
        {
            return safe;
        }

        return Url.Page("/ActionTasks/Index", new { ViewMode = DefaultCollectionView }) ?? "/ActionTasks";
    }

    private string? SafeReturnUrl()
        => !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : null;

    private bool IsStatus(string status) => string.Equals(TaskItem.Status, status, StringComparison.OrdinalIgnoreCase);

    private static string RemarkDraftKey(int taskId, string field)
        => $"ActionTasks.RemarkDraft.{taskId}.{field}";

    private void PreserveRemarkDraft(TaskRemarkInput input)
    {
        if (input.TaskId <= 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(input.Body))
        {
            TempData[RemarkDraftKey(input.TaskId, "Body")] = input.Body;
        }

        if (!string.IsNullOrWhiteSpace(input.UpdateType))
        {
            TempData[RemarkDraftKey(input.TaskId, "Type")] = input.UpdateType;
        }
    }

    private void RestoreRemarkDraft(int taskId)
    {
        var draftBody = TempData[RemarkDraftKey(taskId, "Body")] as string;
        var draftType = TempData[RemarkDraftKey(taskId, "Type")] as string;
        var normalizedType = ActionTaskUpdateTypes.All.FirstOrDefault(type =>
            !string.Equals(type, ActionTaskUpdateTypes.Progress, StringComparison.OrdinalIgnoreCase)
            && string.Equals(type, draftType, StringComparison.OrdinalIgnoreCase));

        RemarkInput.Body = draftBody ?? string.Empty;
        RemarkInput.UpdateType = normalizedType ?? DefaultRemarkType;
    }

    private static byte[] DecodeRowVersion(string rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            throw new InvalidOperationException("Task version is missing. Please reload and try again.");
        }

        try
        {
            return Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Task version is invalid. Please reload and try again.");
        }
    }

    public sealed class ChangeDateInputModel
    {
        [Required]
        public int TaskId { get; set; }

        [Required]
        public string RowVersion { get; set; } = string.Empty;

        [Required]
        public DateTime NewDate { get; set; }

        [Required, StringLength(1000)]
        public string Remarks { get; set; } = string.Empty;
    }
}
