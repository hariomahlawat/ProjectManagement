using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectRemarksPanelService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IClock _clock;

    public ProjectRemarksPanelService(UserManager<ApplicationUser> users, IClock clock)
    {
        _users = users;
        _clock = clock;
    }

    public async Task<ProjectRemarksPanelViewModel> BuildAsync(
        Project project,
        IEnumerable<ProjectStage> stages,
        ClaimsPrincipal userPrincipal,
        CancellationToken ct)
    {
        var stageOptions = stages
            .Where(s => !string.IsNullOrWhiteSpace(s.StageCode))
            .Select(s => s.StageCode!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(code => new ProjectRemarksPanelViewModel.RemarkStageOption(code, BuildStageDisplayName(code)))
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var roleOptions = new[]
            {
                RemarkActorRole.ProjectOfficer,
                RemarkActorRole.HeadOfDepartment,
                RemarkActorRole.Commandant,
                RemarkActorRole.Administrator,
                RemarkActorRole.Mco,
                RemarkActorRole.ProjectOffice,
                RemarkActorRole.MainOffice,
                RemarkActorRole.Ta
            }
            .Select(role =>
            {
                var label = BuildRoleDisplayName(role);
                var canonical = role.ToString();
                return new ProjectRemarksPanelViewModel.RemarkRoleOption(canonical, label, canonical);
            })
            .ToList();

        var scopeOptions = new List<ProjectRemarksPanelViewModel.RemarkScopeOption>
        {
            new(RemarkScope.General.ToString(), "General", RemarkScope.General.ToString())
        };

        if (project.Tot is { Status: ProjectTotStatus.InProgress or ProjectTotStatus.Completed })
        {
            scopeOptions.Add(new ProjectRemarksPanelViewModel.RemarkScopeOption(
                RemarkScope.TransferOfTechnology.ToString(),
                "ToT",
                RemarkScope.TransferOfTechnology.ToString()));
        }

        var today = DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow.UtcDateTime))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var user = await _users.GetUserAsync(userPrincipal);
        if (user is null)
        {
            return new ProjectRemarksPanelViewModel
            {
                ProjectId = project.Id,
                StageOptions = stageOptions,
                RoleOptions = roleOptions,
                ScopeOptions = scopeOptions,
                DefaultScope = RemarkScope.General.ToString(),
                Today = today
            };
        }

        var userRoles = await _users.GetRolesAsync(user);
        var remarkRoleSet = userRoles
            .Select(role => RemarkActorRoleExtensions.TryParse(role, out var parsed) ? parsed : RemarkActorRole.Unknown)
            .Where(role => role != RemarkActorRole.Unknown)
            .ToHashSet();

        var viewerOnly = false;

        if (remarkRoleSet.Count == 0)
        {
            const RemarkActorRole fallbackRole = RemarkActorRole.ProjectOfficer;

            if (!string.IsNullOrWhiteSpace(project.LeadPoUserId)
                && string.Equals(project.LeadPoUserId, user.Id, StringComparison.Ordinal))
            {
                remarkRoleSet.Add(fallbackRole);
            }

            if (!string.IsNullOrWhiteSpace(project.HodUserId)
                && string.Equals(project.HodUserId, user.Id, StringComparison.Ordinal))
            {
                remarkRoleSet.Add(RemarkActorRole.HeadOfDepartment);
            }

            if (remarkRoleSet.Count == 0
                && ProjectAccessGuard.CanViewProject(project, userPrincipal, user.Id))
            {
                remarkRoleSet.Add(fallbackRole);
                viewerOnly = true;
            }
        }

        var remarkRoles = remarkRoleSet.ToList();

        var actorRole = SelectDefaultRemarkRole(remarkRoleSet);
        var actorRoleCanonical = actorRole == RemarkActorRole.Unknown ? null : actorRole.ToString();
        var actorRoleLabel = actorRole == RemarkActorRole.Unknown ? null : BuildRoleDisplayName(actorRole);

        var canOverride = !viewerOnly && remarkRoleSet.Any(role => role is RemarkActorRole.HeadOfDepartment or RemarkActorRole.Commandant or RemarkActorRole.Administrator);
        var canPostAsHoDOrAbove = !viewerOnly && remarkRoleSet.Any(role => role is RemarkActorRole.HeadOfDepartment or RemarkActorRole.Commandant or RemarkActorRole.Administrator);
        var canPostAsMco = !viewerOnly && remarkRoleSet.Contains(RemarkActorRole.Mco);
        var canPostAsPo = !viewerOnly && remarkRoleSet.Contains(RemarkActorRole.ProjectOfficer)
            && !string.IsNullOrWhiteSpace(project.LeadPoUserId)
            && string.Equals(project.LeadPoUserId, user.Id, StringComparison.Ordinal);

        var showComposer = !viewerOnly && (canPostAsHoDOrAbove || canPostAsMco || canPostAsPo);
        var allowExternal = !viewerOnly && canPostAsHoDOrAbove;

        return new ProjectRemarksPanelViewModel
        {
            ProjectId = project.Id,
            CurrentUserId = user.Id,
            ActorDisplayName = DisplayName(user),
            ActorRole = actorRoleCanonical,
            ActorRoleLabel = actorRoleLabel,
            ActorRoles = remarkRoles.Select(role => role.ToString()).ToArray(),
            ShowComposer = showComposer,
            AllowInternal = showComposer,
            AllowExternal = allowExternal,
            ShowDeletedToggle = !viewerOnly && remarkRoleSet.Contains(RemarkActorRole.Administrator),
            ActorHasOverride = canOverride,
            StageOptions = stageOptions,
            RoleOptions = roleOptions,
            ScopeOptions = scopeOptions,
            DefaultScope = RemarkScope.General.ToString(),
            Today = today,
            ViewerOnly = viewerOnly
        };
    }

    private static RemarkActorRole SelectDefaultRemarkRole(IReadOnlyCollection<RemarkActorRole> roles)
    {
        foreach (var candidate in new[]
                 {
                     RemarkActorRole.ProjectOfficer,
                     RemarkActorRole.HeadOfDepartment,
                     RemarkActorRole.Commandant,
                     RemarkActorRole.Administrator,
                     RemarkActorRole.ProjectOffice,
                     RemarkActorRole.MainOffice,
                     RemarkActorRole.Mco,
                     RemarkActorRole.Ta
                 })
        {
            if (roles.Contains(candidate))
            {
                return candidate;
            }
        }

        return RemarkActorRole.Unknown;
    }

    private static string BuildRoleDisplayName(RemarkActorRole role)
        => role switch
        {
            RemarkActorRole.ProjectOfficer => "Project Officer",
            RemarkActorRole.HeadOfDepartment => "HoD",
            RemarkActorRole.Commandant => "Comdt",
            RemarkActorRole.Administrator => "Admin",
            RemarkActorRole.Mco => "MCO",
            RemarkActorRole.ProjectOffice => "Project Office",
            RemarkActorRole.MainOffice => "Main Office",
            RemarkActorRole.Ta => "TA",
            _ => role.ToString()
        };

    private static string BuildStageDisplayName(string? stageCode)
    {
        if (string.IsNullOrWhiteSpace(stageCode))
        {
            return "General";
        }

        return string.Format(CultureInfo.InvariantCulture, "{0} ({1})", StageCodes.DisplayNameOf(stageCode), stageCode);
    }

    private static string DisplayName(ApplicationUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            return user.FullName;
        }

        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            return user.UserName!;
        }

        return user.Email ?? user.Id;
    }
}
