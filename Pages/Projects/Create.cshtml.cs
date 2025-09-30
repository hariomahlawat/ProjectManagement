using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Pages.Projects
{
    [Authorize(Policy = "Project.Create")]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _users;
        private readonly IClock _clock;
        private readonly IAuditService _audit;

        public CreateModel(ApplicationDbContext db, UserManager<ApplicationUser> users, IClock clock, IAuditService audit)
        {
            _db = db;
            _users = users;
            _clock = clock;
            _audit = audit;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public IEnumerable<SelectListItem> TopCategories { get; private set; } = Array.Empty<SelectListItem>();
        public IEnumerable<SelectListItem> HodList { get; private set; } = Array.Empty<SelectListItem>();
        public IEnumerable<SelectListItem> PoList { get; private set; } = Array.Empty<SelectListItem>();
        public IEnumerable<SelectListItem> StageOptions { get; private set; } = Array.Empty<SelectListItem>();
        public IEnumerable<SelectListItem> SponsoringUnitOptions { get; private set; } = Array.Empty<SelectListItem>();
        public IEnumerable<SelectListItem> LineDirectorateOptions { get; private set; } = Array.Empty<SelectListItem>();

        public class InputModel
        {
            [Required]
            [MaxLength(100)]
            public string Name { get; set; } = string.Empty;

            [MaxLength(64)]
            public string? CaseFileNumber { get; set; }

            [MaxLength(1000)]
            public string? Description { get; set; }

            public int? CategoryId { get; set; }

            public int? SubCategoryId { get; set; }

            public string? HodUserId { get; set; }

            public string? PoUserId { get; set; }

            public bool IsOngoing { get; set; }

            public string? LastStageCompleted { get; set; }

            [DataType(DataType.Date)]
            public DateTime? LastStageCompletedOn { get; set; }

            [Display(Name = "Sponsoring Unit")]
            public int? SponsoringUnitId { get; set; }

            [Display(Name = "Sponsoring Line Dte")]
            public int? SponsoringLineDirectorateId { get; set; }
        }

        public async Task OnGetAsync()
        {
            await LoadAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await LoadAsync();

            if (Input.IsOngoing)
            {
                if (string.IsNullOrWhiteSpace(Input.LastStageCompleted))
                {
                    ModelState.AddModelError("Input.LastStageCompleted", "Select the last stage that was completed.");
                }
                else if (!StageCodes.All.Contains(Input.LastStageCompleted, StringComparer.OrdinalIgnoreCase))
                {
                    ModelState.AddModelError("Input.LastStageCompleted", "Unknown stage code.");
                }

                if (Input.LastStageCompletedOn is null)
                {
                    ModelState.AddModelError("Input.LastStageCompletedOn", "Provide the completion date.");
                }
                else
                {
                    var completedDate = DateOnly.FromDateTime(Input.LastStageCompletedOn.Value.Date);
                    var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
                    if (completedDate > today)
                    {
                        ModelState.AddModelError("Input.LastStageCompletedOn", "Completion date cannot be in the future.");
                    }
                }
            }

            string? caseFileNumber = null;
            if (!string.IsNullOrWhiteSpace(Input.CaseFileNumber))
            {
                caseFileNumber = Input.CaseFileNumber.Trim();
                var exists = await _db.Projects.AnyAsync(p => p.CaseFileNumber == caseFileNumber);
                if (exists)
                {
                    ModelState.AddModelError("Input.CaseFileNumber", "Case file number already exists.");
                    return Page();
                }
            }

            if (Input.SponsoringUnitId.HasValue)
            {
                var unitIsActive = await _db.SponsoringUnits
                    .AnyAsync(u => u.Id == Input.SponsoringUnitId.Value && u.IsActive);

                if (!unitIsActive)
                {
                    ModelState.AddModelError("Input.SponsoringUnitId", ProjectValidationMessages.InactiveSponsoringUnit);
                }
            }

            if (Input.SponsoringLineDirectorateId.HasValue)
            {
                var directorateIsActive = await _db.LineDirectorates
                    .AnyAsync(l => l.Id == Input.SponsoringLineDirectorateId.Value && l.IsActive);

                if (!directorateIsActive)
                {
                    ModelState.AddModelError("Input.SponsoringLineDirectorateId", ProjectValidationMessages.InactiveLineDirectorate);
                }
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var currentUserId = _users.GetUserId(User);
            if (string.IsNullOrEmpty(currentUserId))
            {
                return Forbid();
            }

            var categoryId = Input.SubCategoryId ?? Input.CategoryId;

            var project = new Project
            {
                Name = Input.Name.Trim(),
                CaseFileNumber = caseFileNumber,
                Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim(),
                CategoryId = categoryId,
                HodUserId = string.IsNullOrWhiteSpace(Input.HodUserId) ? null : Input.HodUserId,
                LeadPoUserId = string.IsNullOrWhiteSpace(Input.PoUserId) ? null : Input.PoUserId,
                SponsoringUnitId = Input.SponsoringUnitId,
                SponsoringLineDirectorateId = Input.SponsoringLineDirectorateId,
                CreatedByUserId = currentUserId,
                CreatedAt = _clock.UtcNow.UtcDateTime
            };

            _db.Projects.Add(project);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.ConstraintName == "IX_Projects_CaseFileNumber")
            {
                _db.Entry(project).State = EntityState.Detached;
                ModelState.AddModelError("Input.CaseFileNumber", "Case file number already exists.");
                return Page();
            }

            if (Input.IsOngoing && !string.IsNullOrWhiteSpace(Input.LastStageCompleted) && Input.LastStageCompletedOn.HasValue)
            {
                var completedCode = StageCodes.All.First(code => string.Equals(code, Input.LastStageCompleted, StringComparison.OrdinalIgnoreCase));
                var completedDate = DateOnly.FromDateTime(Input.LastStageCompletedOn.Value.Date);
                var stage = new ProjectStage
                {
                    ProjectId = project.Id,
                    StageCode = completedCode,
                    SortOrder = Array.IndexOf(StageCodes.All, completedCode),
                    Status = StageStatus.Completed,
                    ActualStart = completedDate,
                    CompletedOn = completedDate
                };

                _db.ProjectStages.Add(stage);
                await _db.SaveChangesAsync();
            }

            await _audit.LogAsync(
                "Projects.Created",
                data: new Dictionary<string, string?>
                {
                    ["ProjectId"] = project.Id.ToString(),
                    ["Name"] = project.Name,
                    ["CaseFileNumber"] = project.CaseFileNumber,
                    ["CategoryId"] = project.CategoryId?.ToString(),
                    ["HodUserId"] = project.HodUserId,
                    ["LeadPoUserId"] = project.LeadPoUserId,
                    ["SponsoringUnitId"] = project.SponsoringUnitId?.ToString(),
                    ["SponsoringLineDirectorateId"] = project.SponsoringLineDirectorateId?.ToString()
                },
                userId: currentUserId,
                userName: User.Identity?.Name
            );

            return RedirectToPage("/Projects/Overview", new { id = project.Id });
        }

        private async Task LoadAsync()
        {
            TopCategories = await _db.ProjectCategories
                .Where(c => c.ParentId == null && c.IsActive)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
                .ToListAsync();

            var hodUsers = await _users.GetUsersInRoleAsync("HoD");
            HodList = BuildUserOptions(hodUsers);

            var poUsers = await _users.GetUsersInRoleAsync("Project Officer");
            PoList = BuildUserOptions(poUsers);

            StageOptions = StageCodes.All
                .Select(code => new SelectListItem(code, code))
                .ToList();

            await LoadSponsoringLookupsAsync();
        }

        private async Task LoadSponsoringLookupsAsync()
        {
            var unitOptions = await _db.SponsoringUnits
                .Where(u => u.IsActive)
                .OrderBy(u => u.SortOrder)
                .ThenBy(u => u.Name)
                .Select(u => new SelectListItem(u.Name, u.Id.ToString()))
                .ToListAsync();

            var directorateOptions = await _db.LineDirectorates
                .Where(l => l.IsActive)
                .OrderBy(l => l.SortOrder)
                .ThenBy(l => l.Name)
                .Select(l => new SelectListItem(l.Name, l.Id.ToString()))
                .ToListAsync();

            SponsoringUnitOptions = PrependEmpty(unitOptions, Input.SponsoringUnitId);
            LineDirectorateOptions = PrependEmpty(directorateOptions, Input.SponsoringLineDirectorateId);
        }

        private static IEnumerable<SelectListItem> PrependEmpty(IEnumerable<SelectListItem> items, int? selectedValue)
        {
            var list = new List<SelectListItem>
            {
                new("— (none) —", string.Empty, selectedValue is null)
            };

            var selectedString = selectedValue?.ToString();
            list.AddRange(items.Select(item =>
            {
                var clone = new SelectListItem(item.Text, item.Value)
                {
                    Selected = selectedString is not null && string.Equals(item.Value, selectedString, StringComparison.Ordinal)
                };
                return clone;
            }));

            return list;
        }

        private static IEnumerable<SelectListItem> BuildUserOptions(IEnumerable<ApplicationUser> users)
        {
            var items = new List<SelectListItem>
            {
                new("— Unassigned —", string.Empty)
            };

            items.AddRange(users
                .OrderBy(u => string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName)
                .Select(u => new SelectListItem(DisplayName(u), u.Id)));

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
    }
}
