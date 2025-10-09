using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services;
using ProjectManagement.Services.Notifications;

namespace ProjectManagement.Pages.Projects
{
    [Authorize(Roles = "Admin,HoD")]
    public class AssignRolesModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _users;
        private readonly IAuditService _audit;
        private readonly INotificationPublisher _notifications;

        public AssignRolesModel(
            ApplicationDbContext db,
            UserManager<ApplicationUser> users,
            IAuditService audit,
            INotificationPublisher notifications)
        {
            _db = db;
            _users = users;
            _audit = audit;
            _notifications = notifications;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string ProjectName { get; private set; } = string.Empty;
        public IEnumerable<SelectListItem> HodList { get; private set; } = Array.Empty<SelectListItem>();
        public IEnumerable<SelectListItem> PoList { get; private set; } = Array.Empty<SelectListItem>();

        public class InputModel
        {
            public int ProjectId { get; set; }
            public string? HodUserId { get; set; }
            public string? PoUserId { get; set; }
            public string? RowVersion { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            if (project is null)
            {
                return NotFound();
            }

            ProjectName = project.Name;
            Input.ProjectId = project.Id;
            Input.HodUserId = project.HodUserId;
            Input.PoUserId = project.LeadPoUserId;
            Input.RowVersion = Convert.ToBase64String(project.RowVersion);

            await LoadListsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == Input.ProjectId);
            if (project is null)
            {
                return NotFound();
            }

            var previousHod = project.HodUserId;
            var previousPo = project.LeadPoUserId;

            if (!string.IsNullOrEmpty(Input.RowVersion))
            {
                try
                {
                    var rowVersion = Convert.FromBase64String(Input.RowVersion);
                    _db.Entry(project).Property(p => p.RowVersion).OriginalValue = rowVersion;
                }
                catch (FormatException)
                {
                    TempData["Error"] = "Unable to process the request. Please reload and try again.";
                    TempData["OpenOffcanvas"] = "assign-roles";
                    return RedirectToPage("/Projects/Overview", new { id = project.Id });
                }
            }
            else
            {
                TempData["Error"] = "Unable to process the request. Please reload and try again.";
                TempData["OpenOffcanvas"] = "assign-roles";
                return RedirectToPage("/Projects/Overview", new { id = project.Id });
            }

            project.HodUserId = string.IsNullOrWhiteSpace(Input.HodUserId) ? null : Input.HodUserId;
            project.LeadPoUserId = string.IsNullOrWhiteSpace(Input.PoUserId) ? null : Input.PoUserId;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                TempData["Error"] = "The project was updated by someone else. Please reload and try again.";
                TempData["OpenOffcanvas"] = "assign-roles";
                return RedirectToPage("/Projects/Overview", new { id = project.Id });
            }

            var currentUserId = _users.GetUserId(User);
            var currentUserName = User.Identity?.Name;

            await _audit.LogAsync(
                "Projects.AssignRoles",
                data: new Dictionary<string, string?>
                {
                    ["ProjectId"] = project.Id.ToString(),
                    ["ProjectName"] = project.Name,
                    ["PreviousHodUserId"] = previousHod,
                    ["NewHodUserId"] = project.HodUserId,
                    ["PreviousPoUserId"] = previousPo,
                    ["NewPoUserId"] = project.LeadPoUserId
                },
                userId: currentUserId,
                userName: currentUserName
            );

            await PublishProjectOfficerChangeAsync(
                project,
                previousPo,
                currentUserId);

            TempData["Flash"] = "Roles updated.";

            return RedirectToPage("/Projects/Overview", new { id = project.Id });
        }

        private async Task LoadListsAsync()
        {
            var hodUsers = await _users.GetUsersInRoleAsync("HoD");
            HodList = BuildUserOptions(hodUsers, Input.HodUserId);

            var poUsers = await _users.GetUsersInRoleAsync("Project Officer");
            PoList = BuildUserOptions(poUsers, Input.PoUserId);
        }

        private static IEnumerable<SelectListItem> BuildUserOptions(IEnumerable<ApplicationUser> users, string? selectedUserId)
        {
            var items = new List<SelectListItem>
            {
                new("— Unassigned —", string.Empty)
            };

            items.AddRange(users
                .OrderBy(u => string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName)
                .Select(u => new SelectListItem(DisplayName(u), u.Id)));

            var selectedValue = selectedUserId ?? string.Empty;

            foreach (var item in items)
            {
                item.Selected = string.Equals(item.Value, selectedValue, StringComparison.Ordinal);
            }

            if (items.All(i => !i.Selected))
            {
                items[0].Selected = true;
            }

            return items;
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

        private async Task PublishProjectOfficerChangeAsync(
            Project project,
            string? previousPoUserId,
            string? actorUserId)
        {
            if (string.Equals(previousPoUserId, project.LeadPoUserId, StringComparison.Ordinal))
            {
                return;
            }

            var recipients = new List<string>();

            if (!string.IsNullOrWhiteSpace(previousPoUserId))
            {
                recipients.Add(previousPoUserId);
            }

            if (!string.IsNullOrWhiteSpace(project.LeadPoUserId))
            {
                recipients.Add(project.LeadPoUserId);
            }

            if (recipients.Count == 0)
            {
                return;
            }

            var previousPoName = await GetUserDisplayNameAsync(previousPoUserId);
            var currentPoName = await GetUserDisplayNameAsync(project.LeadPoUserId);

            var payload = new ProjectAssignmentChangedNotificationPayload(
                project.Id,
                project.Name,
                previousPoUserId,
                previousPoName,
                project.LeadPoUserId,
                currentPoName);

            var summary = string.Format(
                CultureInfo.InvariantCulture,
                "Project officer assignment changed from {0} to {1}. Review the project overview for details.",
                previousPoName ?? "Unassigned",
                currentPoName ?? "Unassigned");

            await _notifications.PublishAsync(
                NotificationKind.ProjectAssignmentChanged,
                recipients,
                payload,
                module: "Projects",
                eventType: "ProjectOfficerAssignmentChanged",
                scopeType: "Project",
                scopeId: project.Id.ToString(CultureInfo.InvariantCulture),
                projectId: project.Id,
                actorUserId: actorUserId,
                route: $"/projects/overview/{project.Id}",
                title: string.IsNullOrWhiteSpace(project.Name)
                    ? "Project officer assignment updated"
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} project officer updated",
                        project.Name),
                summary: summary,
                fingerprint: null);
        }

        private async Task<string?> GetUserDisplayNameAsync(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            var user = await _users.FindByIdAsync(userId);
            return user is null ? null : DisplayName(user);
        }
    }
}
