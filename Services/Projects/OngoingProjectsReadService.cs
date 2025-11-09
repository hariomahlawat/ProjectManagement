using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Projects;
using ProjectManagement.Models.Stages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Projects
{
    /// <summary>
    /// Read-only service to fetch all active/ongoing projects with their stage/timeline info.
    /// Supports filtering by Lead Project Officer (LeadPoUserId) and returns officer display name.
    /// </summary>
    public sealed class OngoingProjectsReadService
    {
        private readonly ApplicationDbContext _db;

        public OngoingProjectsReadService(ApplicationDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<IReadOnlyList<OngoingProjectRowDto>> GetAsync(
            int? projectCategoryId,
            string? leadPoUserId,     // filter by officer (user id)
            string? search,
            CancellationToken cancellationToken)
        {
            // 1. active / ongoing projects only
            var q = _db.Projects
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.LeadPoUser)  // we need name
                .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Active
                            && !p.IsArchived
                            && !p.IsDeleted);

            if (projectCategoryId is { } catId && catId > 0)
            {
                q = q.Where(p => p.CategoryId == catId);
            }

            if (!string.IsNullOrWhiteSpace(leadPoUserId))
            {
                var officerId = leadPoUserId.Trim();
                q = q.Where(p => p.LeadPoUserId == officerId);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                q = q.Where(p => p.Name.Contains(term));
            }

            var projects = await q
                .OrderBy(p => p.Name)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.CategoryId,
                    CategoryName = p.Category != null ? p.Category.Name : null,
                    p.LeadPoUserId,
                    // 👇 change "FullName" below if your ApplicationUser uses another property
                    LeadPoName = p.LeadPoUser != null ? p.LeadPoUser.FullName ?? p.LeadPoUser.UserName : null
                })
                .ToListAsync(cancellationToken);

            if (projects.Count == 0)
            {
                return Array.Empty<OngoingProjectRowDto>();
            }

            var projectIds = projects.Select(p => p.Id).ToArray();

            // 2. load all stage rows for these projects
            var allStages = await _db.ProjectStages
                .AsNoTracking()
                .Where(s => projectIds.Contains(s.ProjectId))
                .ToListAsync(cancellationToken);

            var result = new List<OngoingProjectRowDto>(projects.Count);

            foreach (var proj in projects)
            {
                var stagesForProject = allStages
                    .Where(s => s.ProjectId == proj.Id)
                    .ToDictionary(s => s.StageCode, s => s);

                var stageDtos = new List<OngoingProjectStageDto>(StageCodes.All.Length);

                int? inProgressIndex = null;
                int lastCompletedIndex = -1;

                for (var i = 0; i < StageCodes.All.Length; i++)
                {
                    var code = StageCodes.All[i];
                    stagesForProject.TryGetValue(code, out var stageRow);

                    var status = StageStatus.NotStarted;
                    DateOnly? actualCompleted = null;
                    DateOnly? plannedDue = null;
                    var isDataMissing = false;

                    if (stageRow != null)
                    {
                        status = stageRow.Status;
                        actualCompleted = stageRow.CompletedOn;
                        plannedDue = stageRow.PlannedDue;
                    }
                    else
                    {
                        isDataMissing = true;
                    }

                    if (status == StageStatus.InProgress && inProgressIndex == null)
                    {
                        inProgressIndex = i;
                    }

                    if (status == StageStatus.Completed)
                    {
                        lastCompletedIndex = i;
                    }

                    stageDtos.Add(new OngoingProjectStageDto
                    {
                        Code = code,
                        Name = code, // StageCodes has no GetDisplayName
                        Status = status,
                        ActualCompletedOn = actualCompleted,
                        PlannedDue = plannedDue,
                        IsDataMissing = isDataMissing,
                        IsCurrent = false
                    });
                }

                // determine current
                int currentIndex;
                if (inProgressIndex.HasValue)
                {
                    currentIndex = inProgressIndex.Value;
                }
                else if (lastCompletedIndex >= 0 && lastCompletedIndex + 1 < stageDtos.Count)
                {
                    currentIndex = lastCompletedIndex + 1;
                }
                else
                {
                    currentIndex = 0;
                }

                stageDtos[currentIndex].IsCurrent = true;

                string? lastCompletedName = null;
                DateOnly? lastCompletedDate = null;
                if (lastCompletedIndex >= 0)
                {
                    lastCompletedName = stageDtos[lastCompletedIndex].Name;
                    lastCompletedDate = stageDtos[lastCompletedIndex].ActualCompletedOn;
                }

                result.Add(new OngoingProjectRowDto
                {
                    ProjectId = proj.Id,
                    ProjectName = proj.Name,
                    ProjectCategoryId = proj.CategoryId,
                    ProjectCategoryName = proj.CategoryName,
                    // officer
                    LeadPoUserId = proj.LeadPoUserId,
                    LeadPoName = proj.LeadPoName,
                    CurrentStageCode = stageDtos[currentIndex].Code,
                    CurrentStageName = stageDtos[currentIndex].Name,
                    LastCompletedStageName = lastCompletedName,
                    LastCompletedStageDate = lastCompletedDate,
                    Stages = stageDtos
                });
            }

            return result;
        }

        /// <summary>
        /// Build officer dropdown from all active projects that have a LeadPoUser.
        /// Value = LeadPoUserId, Text = officer name.
        /// </summary>
        public async Task<IReadOnlyList<SelectListItem>> GetProjectOfficerOptionsAsync(
            string? selectedOfficerId,
            CancellationToken cancellationToken)
        {
            var officers = await _db.Projects
                .AsNoTracking()
                .Include(p => p.LeadPoUser)
                .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Active
                            && !p.IsArchived
                            && !p.IsDeleted
                            && p.LeadPoUserId != null)
                .Select(p => new
                {
                    p.LeadPoUserId,
                    // 👇 change "FullName" here too if needed
                    Name = p.LeadPoUser != null ? p.LeadPoUser.FullName ?? p.LeadPoUser.UserName : p.LeadPoUserId
                })
                .Distinct()
                .OrderBy(x => x.Name)
                .ToListAsync(cancellationToken);

            var items = new List<SelectListItem>
            {
                new("All officers", string.Empty)
            };

            foreach (var o in officers)
            {
                items.Add(new SelectListItem(
                    o.Name ?? o.LeadPoUserId!,
                    o.LeadPoUserId!,
                    string.Equals(o.LeadPoUserId, selectedOfficerId, StringComparison.Ordinal)));
            }

            return items;
        }
    }

    public sealed class OngoingProjectRowDto
    {
        public int ProjectId { get; init; }
        public string ProjectName { get; init; } = "";
        public int? ProjectCategoryId { get; init; }
        public string? ProjectCategoryName { get; init; }

        // officer
        public string? LeadPoUserId { get; init; }
        public string? LeadPoName { get; init; }

        public string CurrentStageCode { get; init; } = "";
        public string? CurrentStageName { get; init; }
        public string? LastCompletedStageName { get; init; }
        public DateOnly? LastCompletedStageDate { get; init; }

        public IReadOnlyList<OngoingProjectStageDto> Stages { get; init; } = Array.Empty<OngoingProjectStageDto>();
    }

    public sealed class OngoingProjectStageDto
    {
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public StageStatus Status { get; init; }
        public DateOnly? ActualCompletedOn { get; init; }
        public DateOnly? PlannedDue { get; init; }
        public bool IsDataMissing { get; init; }
        public bool IsCurrent { get; set; }
    }
}
