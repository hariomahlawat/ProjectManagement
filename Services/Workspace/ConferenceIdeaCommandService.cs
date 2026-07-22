using Microsoft.AspNetCore.Identity;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Services.ProjectIdeas;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

/// <summary>
/// Creates a normal Project Idea from the officer conference review. Project Ideas
/// remain the authoritative persistence boundary; the conference page supplies only
/// a compact creation workflow for the officer currently under review.
/// </summary>
public sealed class ConferenceIdeaCommandService : IConferenceIdeaCommandService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IOfficerWorkloadReadService _workload;
    private readonly IProjectIdeaCommandService _ideas;
    private readonly IClock _clock;
    private readonly ILogger<ConferenceIdeaCommandService> _logger;

    public ConferenceIdeaCommandService(
        UserManager<ApplicationUser> users,
        IOfficerWorkloadReadService workload,
        IProjectIdeaCommandService ideas,
        IClock clock,
        ILogger<ConferenceIdeaCommandService> logger)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _workload = workload ?? throw new ArgumentNullException(nameof(workload));
        _ideas = ideas ?? throw new ArgumentNullException(nameof(ideas));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CreateConferenceIdeaResult> CreateAsync(
        string actorUserId,
        CreateConferenceIdeaRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new UnauthorizedAccessException("A signed-in command user is required.");
        }

        ArgumentNullException.ThrowIfNull(request);

        var title = request.Title?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;
        if (title.Length is < 1 or > 200)
        {
            throw new InvalidOperationException("Idea title is required and cannot exceed 200 characters.");
        }

        if (description.Length is < 1 or > 2000)
        {
            throw new InvalidOperationException("Concept or problem statement is required and cannot exceed 2000 characters.");
        }

        var actor = await _users.FindByIdAsync(actorUserId)
            ?? throw new UnauthorizedAccessException("The command user account is unavailable.");
        if (!IsActive(actor))
        {
            throw new UnauthorizedAccessException("The command user account is not active.");
        }

        var actorRoles = await _users.GetRolesAsync(actor);
        var isHod = actorRoles.Contains(RoleNames.HoD, StringComparer.OrdinalIgnoreCase);
        var isComdt = actorRoles.Contains(RoleNames.Comdt, StringComparer.OrdinalIgnoreCase);
        if (!isHod && !isComdt)
        {
            throw new UnauthorizedAccessException("Only Comdt or HoD can create an idea from the conference review.");
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
        EnsureActive(officer, "The selected officer account is not active.");

        var officerRoles = await _users.GetRolesAsync(officer);
        if (!officerRoles.Contains(RoleNames.ProjectOfficer, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected officer is not authorised to hold Project Ideas.");
        }

        var assignedHod = isHod
            ? actor
            : await ResolveSelectedHodAsync(request.AssignedHodUserId);

        var idea = await _ideas.CreateAsync(
            new ProjectIdea
            {
                Title = title,
                Description = description,
                Status = ProjectIdeaStatuses.Active,
                AssignedProjectOfficerUserId = officer.Id,
                AssignedHodUserId = assignedHod.Id,
                CreatedByUserId = actor.Id
            },
            cancellationToken);

        _logger.LogInformation(
            "Project Idea {IdeaId} created from Officer Conference Review by {ActorUserId} for officer {OfficerUserId} under HoD {HodUserId}.",
            idea.Id,
            actor.Id,
            officer.Id,
            assignedHod.Id);

        return new CreateConferenceIdeaResult(new OfficerConferenceItemVm
        {
            Kind = ConferenceItemKind.ProjectIdea,
            ItemId = idea.Id,
            Title = idea.Title,
            OpenUrl = $"/ProjectIdeas/Details/{idea.Id}",
            CurrentStateCode = idea.Status,
            CurrentStateName = ProjectIdeaStatuses.ToDisplay(idea.Status),
            CurrentContext = "Created just now",
            AttentionText = null,
            RequiresAttention = false,
            LatestDirection = null,
            ProgressEntries = Array.Empty<ConferenceProgressEntryVm>(),
            EmptyProgressText = null,
            ProgressSummary = string.Empty,
            LatestProgressText = null
        });
    }

    private async Task<ApplicationUser> ResolveSelectedHodAsync(string? selectedHodUserId)
    {
        if (string.IsNullOrWhiteSpace(selectedHodUserId))
        {
            throw new InvalidOperationException("Select the HoD responsible for oversight of this idea.");
        }

        var hod = await _users.FindByIdAsync(selectedHodUserId.Trim())
            ?? throw new InvalidOperationException("The selected HoD account is unavailable.");
        EnsureActive(hod, "The selected HoD account is not active.");

        var roles = await _users.GetRolesAsync(hod);
        if (!roles.Contains(RoleNames.HoD, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Select a valid HoD for oversight of this idea.");
        }

        return hod;
    }

    private void EnsureActive(ApplicationUser user, string message)
    {
        if (!IsActive(user))
        {
            throw new InvalidOperationException(message);
        }
    }

    private bool IsActive(ApplicationUser user)
        => !user.IsDisabled
            && !user.PendingDeletion
            && (!user.LockoutEnd.HasValue || user.LockoutEnd.Value <= _clock.UtcNow);
}
