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
    public RemarkInputModel RemarkInput { get; set; } = new();

    [BindProperty]
    public ChangeDateInputModel ChangeDateInput { get; set; } = new();

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
    public bool CanAddRemark => !IsClosed && _permission.CanAddTaskUpdate(CurrentRole, CurrentUserId, TaskItem.AssignedToUserId);
    public bool CanAddConference => !IsClosed && _permission.CanAddConferenceUpdate(CurrentRole);
    public bool CanUpdateStatus => _workflow.CanUpdateTaskStatus(TaskItem, CurrentRole, CurrentUserId);
    public bool CanSubmit => IsInProgress && _workflow.CanSubmitTask(TaskItem, CurrentUserId);
    public bool CanCommandClose => _permission.CanCloseTaskDirectly(TaskItem, CurrentRole);
    public bool CanReturnForAction => IsSubmitted && _workflow.CanReturnTaskForAction(TaskItem, CurrentRole);
    public bool CanChangeDate => _workflow.CanChangeTaskDate(TaskItem, CurrentRole);
    public bool CanManagePlanning => _permission.CanManageSprints(CurrentRole);
    public bool CanViewSystemHistory => _workflow.CanViewSystemHistory(CurrentRole);
    public IReadOnlyList<ActionSprint> AssignableSprints => Sprints.Where(sprint => sprint.Status != ActionSprintStatus.Closed).ToList();
    public bool CanRemoveFromSprint => CanManagePlanning
        && !IsClosed
        && TaskItem.SprintId.HasValue
        && Sprints.Any(sprint => sprint.Id == TaskItem.SprintId.Value && sprint.Status != ActionSprintStatus.Closed);
    public bool CanMoveToBacklog => CanManagePlanning
        && !IsClosed
        && !IsBacklog
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
        RemarkInput.UpdateType = DefaultRemarkType;
        ChangeDateInput.TaskId = id;
        ChangeDateInput.RowVersion = Convert.ToBase64String(TaskItem.RowVersion);
        ChangeDateInput.NewDate = TaskItem.DueDate.Date >= IstToday ? TaskItem.DueDate.Date : IstToday;
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

        var normalizedType = ActionTaskUpdateTypes.All.FirstOrDefault(type => string.Equals(type, RemarkInput.UpdateType, StringComparison.OrdinalIgnoreCase));
        if (normalizedType is null || string.Equals(normalizedType, ActionTaskUpdateTypes.Progress, StringComparison.OrdinalIgnoreCase))
        {
            TempData["ToastError"] = "Invalid remark type.";
            return RedirectToPage(new { id = Id, returnUrl = SafeReturnUrl() });
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
            TempData["ToastError"] = "Enter the conference direction or observation.";
            return RedirectToPage(new { id = Id, intent = "remark", returnUrl = SafeReturnUrl() });
        }

        if (!hasBody && !hasFiles)
        {
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
            TempData["ToastError"] = ex.Message;
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

    public async Task<IActionResult> OnPostCloseAsync(int id, string rowVersion, string? remarks)
    {
        await ResolveIdentityAsync();
        var reopenIntent = (string?)null;
        try
        {
            await _service.CloseTaskDirectlyAsync(id, DecodeRowVersion(rowVersion), remarks ?? string.Empty, CurrentUserId, CurrentRole);
            TempData["ToastMessage"] = "Task closed successfully.";
        }
        catch (ActionTaskConcurrencyException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "close";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
            reopenIntent = "close";
        }

        return RedirectToPage(new { id, intent = reopenIntent, returnUrl = SafeReturnUrl() });
    }

    public async Task<IActionResult> OnPostChangeDateAsync()
    {
        await ResolveIdentityAsync();
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
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        return RedirectToPage(new { id = ChangeDateInput.TaskId, returnUrl = SafeReturnUrl() });
    }

    public async Task<IActionResult> OnPostAssignBacklogToSprintAsync(int id, int sprintId, string responsibleUserId)
    {
        await ResolveIdentityAsync();
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
        }

        return RedirectToPage(new { id, returnUrl = SafeReturnUrl() });
    }

    public async Task<IActionResult> OnPostAssignOutsideToSprintAsync(int id, int sprintId)
    {
        await ResolveIdentityAsync();
        try
        {
            await _sprintService.AssignOutsideSprintTaskToSprintAsync(id, sprintId, CurrentUserId, CurrentRole);
            TempData["ToastMessage"] = "Task added to sprint.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        return RedirectToPage(new { id, returnUrl = SafeReturnUrl() });
    }

    public async Task<IActionResult> OnPostRemoveFromSprintAsync(int id, string? remarks)
    {
        await ResolveIdentityAsync();
        try
        {
            await _sprintService.RemoveTaskFromSprintKeepAssignedAsync(id, CurrentUserId, CurrentRole, remarks);
            TempData["ToastMessage"] = "Task removed from sprint and kept assigned.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        return RedirectToPage(new { id, returnUrl = SafeReturnUrl() });
    }

    public async Task<IActionResult> OnPostMoveToBacklogAsync(int id, string? remarks)
    {
        await ResolveIdentityAsync();
        try
        {
            await _sprintService.MoveTaskToBacklogRemoveAssigneeAsync(id, CurrentUserId, CurrentRole, remarks);
            TempData["ToastMessage"] = "Task moved to backlog.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        return RedirectToPage(new { id, returnUrl = SafeReturnUrl() });
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
        if (CanManagePlanning)
        {
            AssignableUsers = await _userLookup.LoadAssignableUsersAsync(CurrentRole);
        }

        return true;
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

    public sealed class RemarkInputModel
    {
        [Required]
        public int TaskId { get; set; }

        [StringLength(4000)]
        public string Body { get; set; } = string.Empty;

        [Required, StringLength(32)]
        public string UpdateType { get; set; } = ActionTaskUpdateTypes.Comment;

        public List<IFormFile> Files { get; set; } = new();
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
