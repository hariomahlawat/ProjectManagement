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
            string? search,
            CancellationToken cancellationToken)
        {
            // 1. active / ongoing projects only
            var q = _db.Projects
                .AsNoTracking()
                .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Active
                            && !p.IsArchived
                            && !p.IsDeleted);

            if (projectCategoryId is { } catId && catId > 0)
            {
                q = q.Where(p => p.CategoryId == catId);
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
                    CategoryName = p.Category != null ? p.Category.Name : null
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
                        // your StageCodes doesn’t have GetDisplayName, so we fall back to code itself
                        Name = code,
                        Status = status,
                        ActualCompletedOn = actualCompleted,
                        PlannedDue = plannedDue,
                        IsDataMissing = isDataMissing,
                        IsCurrent = false
                    });
                }

                // pick current stage with your rule
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
                    CurrentStageCode = stageDtos[currentIndex].Code,
                    CurrentStageName = stageDtos[currentIndex].Name,
                    LastCompletedStageName = lastCompletedName,
                    LastCompletedStageDate = lastCompletedDate,
                    Stages = stageDtos
                });
            }

            return result;
        }
    }

    public sealed class OngoingProjectRowDto
    {
        public int ProjectId { get; init; }
        public string ProjectName { get; init; } = "";
        public int? ProjectCategoryId { get; init; }
        public string? ProjectCategoryName { get; init; }

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
