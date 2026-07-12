using Microsoft.AspNetCore.Identity;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

/// <summary>
/// Creates a normal Action Tracker task from the officer conference review without
/// introducing conference-specific task persistence. The selected conference officer
/// remains the authoritative assignee and the existing task service owns persistence,
/// auditing and notifications.
/// </summary>
public sealed class ConferenceTaskCommandService : IConferenceTaskCommandService
{
    private const string ConferenceAuditRemarks = "Created from Officer Conference Review.";

    private static readonly string[] PriorityOrder =
    {
        "Low",
        "Normal",
        "High",
        "Critical"
    };

    private readonly UserManager<ApplicationUser> _users;
    private readonly IOfficerWorkloadReadService _workload;
    private readonly ActionTaskPermissionService _permission;
    private readonly IActionTaskService _tasks;
    private readonly IActionTrackerClock _clock;

    public ConferenceTaskCommandService(
        UserManager<ApplicationUser> users,
        IOfficerWorkloadReadService workload,
        ActionTaskPermissionService permission,
        IActionTaskService tasks,
        IActionTrackerClock clock)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _workload = workload ?? throw new ArgumentNullException(nameof(workload));
        _permission = permission ?? throw new ArgumentNullException(nameof(permission));
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<CreateConferenceTaskResult> CreateAsync(
        string actorUserId,
        CreateConferenceTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new UnauthorizedAccessException("A signed-in command user is required.");
        }

        ArgumentNullException.ThrowIfNull(request);

        var title = request.Title?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;
        var priority = NormalizePriority(request.Priority);
        var dueDate = request.DueDate.Date;

        if (title.Length is < 1 or > 200)
        {
            throw new InvalidOperationException("Task title is required and cannot exceed 200 characters.");
        }

        if (description.Length is < 1 or > 4000)
        {
            throw new InvalidOperationException("Task brief is required and cannot exceed 4000 characters.");
        }

        if (dueDate < _clock.IstToday)
        {
            throw new InvalidOperationException("Due date cannot be in the past.");
        }

        var actor = await _users.FindByIdAsync(actorUserId)
            ?? throw new UnauthorizedAccessException("The command user account is unavailable.");
        var actorRoles = await _users.GetRolesAsync(actor);
        var actorRole = ActionTaskRoleResolver.ResolveFromRoles(actorRoles);
        if (actorRole is null || !_permission.CanCreate(actorRole))
        {
            throw new UnauthorizedAccessException("Only Comdt or HoD can assign a task from the conference review.");
        }

        var conferenceOfficers = await _workload.GetAllAsync(actorUserId, cancellationToken);
        var selectedOfficer = conferenceOfficers.FirstOrDefault(officer => string.Equals(
            officer.UserId,
            request.OfficerUserId,
            StringComparison.Ordinal));
        if (selectedOfficer is null)
        {
            throw new InvalidOperationException("The selected officer is not available in the current conference workload.");
        }

        var officer = await _users.FindByIdAsync(selectedOfficer.UserId)
            ?? throw new InvalidOperationException("The selected officer account is unavailable.");
        if (officer.IsDisabled || officer.PendingDeletion)
        {
            throw new InvalidOperationException("The selected officer account is not active.");
        }

        if (officer.LockoutEnd.HasValue
            && officer.LockoutEnd.Value > new DateTimeOffset(_clock.UtcNow, TimeSpan.Zero))
        {
            throw new InvalidOperationException("The selected officer account is currently locked.");
        }

        var officerRoles = await _users.GetRolesAsync(officer);
        var assignedRole = ResolveAssignableRole(actorRole, officerRoles);
        if (assignedRole is null)
        {
            throw new InvalidOperationException("The selected officer cannot be assigned an Action Tracker task by the current authority.");
        }

        var task = await _tasks.CreateTaskAsync(
            new ActionTaskItem
            {
                Title = title,
                Description = description,
                CreatedByUserId = actorUserId,
                AssignedToUserId = officer.Id,
                CreatedByRole = actorRole,
                AssignedToRole = assignedRole,
                DueDate = dueDate,
                Priority = priority
            },
            ConferenceAuditRemarks,
            cancellationToken);

        var taskDueDate = DateOnly.FromDateTime(task.DueDate);
        return new CreateConferenceTaskResult(new OfficerConferenceItemVm
        {
            Kind = ConferenceItemKind.ActionTask,
            ItemId = task.Id,
            Title = task.Title,
            OpenUrl = $"/ActionTasks/Index?taskId={task.Id}",
            CurrentStateCode = task.Status,
            CurrentStateName = task.Status,
            CurrentContext = $"Due {taskDueDate:dd MMM yyyy} · {task.Priority} priority",
            AttentionText = null,
            RequiresAttention = false,
            LatestDirection = null,
            ProgressEntries = Array.Empty<ConferenceProgressEntryVm>(),
            EmptyProgressText = null,
            ProgressSummary = string.Empty,
            LatestProgressText = null
        });
    }

    private string? ResolveAssignableRole(string actorRole, IEnumerable<string> officerRoles)
    {
        var roleSet = new HashSet<string>(officerRoles, StringComparer.OrdinalIgnoreCase);
        return ActionTaskRoleResolver.AllowedAssignmentRoles()
            .FirstOrDefault(role => roleSet.Contains(role) && _permission.CanAssign(actorRole, role));
    }

    private static string NormalizePriority(string? priority)
    {
        var match = PriorityOrder.FirstOrDefault(value => string.Equals(
            value,
            priority?.Trim(),
            StringComparison.OrdinalIgnoreCase));
        return match ?? throw new InvalidOperationException("Select a valid task priority.");
    }
}
