using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Projects;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Projects
{
    /// <summary>
    /// Read-only service to fetch all active/ongoing projects with their stage/timeline info
    /// plus latest remarks.
    /// </summary>
    public sealed class OngoingProjectsReadService
    {
        private readonly ApplicationDbContext _db;
        private readonly IWorkflowStageMetadataProvider _workflowStageMetadataProvider;

        public OngoingProjectsReadService(ApplicationDbContext db, IWorkflowStageMetadataProvider workflowStageMetadataProvider)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _workflowStageMetadataProvider = workflowStageMetadataProvider ?? throw new ArgumentNullException(nameof(workflowStageMetadataProvider));
        }

        public async Task<IReadOnlyList<OngoingProjectRowDto>> GetAsync(
            int? projectCategoryId,
            string? leadPoUserId,
            string? search,
            CancellationToken cancellationToken)
        {
            // base query – active projects only
            var q = _db.Projects
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.LeadPoUser)
                .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Active
                            && !p.IsArchived
                            && !p.IsDeleted);

            // -------------------- Category filtering --------------------
            if (projectCategoryId is { } catId && catId > 0)
            {
                var categoryScopeIds = await GetCategoryScopeIdsAsync(catId, cancellationToken);
                q = q.Where(p => p.CategoryId.HasValue && categoryScopeIds.Contains(p.CategoryId.Value));
            }

            if (!string.IsNullOrWhiteSpace(leadPoUserId))
            {
                var officerId = leadPoUserId.Trim();
                q = q.Where(p => p.LeadPoUserId == officerId);
            }

            // -------------------- Search filtering (case-insensitive) --------------------
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                var like = $"%{term}%";

                q = q.Where(p => EF.Functions.ILike(p.Name, like));
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
                    LeadPoName = p.LeadPoUser != null
                        ? (p.LeadPoUser.FullName ?? p.LeadPoUser.UserName)
                        : null,
                    p.WorkflowVersion,
                    p.CoverPhotoId,
                    p.CoverPhotoVersion
                })
                .ToListAsync(cancellationToken);

            if (projects.Count == 0)
            {
                return Array.Empty<OngoingProjectRowDto>();
            }

            var projectIds = projects.Select(p => p.Id).ToArray();

            // SECTION: Cover photo versions (batched read to avoid N+1)
            var coverPhotoIds = projects
                .Where(x => x.CoverPhotoId.HasValue)
                .Select(x => x.CoverPhotoId!.Value)
                .Distinct()
                .ToArray();

            var coverPhotoVersionByProject = new Dictionary<int, int?>();
            if (coverPhotoIds.Length > 0)
            {
                var coverPhotos = await _db.ProjectPhotos
                    .AsNoTracking()
                    .Where(p => projectIds.Contains(p.ProjectId) && coverPhotoIds.Contains(p.Id))
                    .Select(p => new { p.ProjectId, p.Version })
                    .ToListAsync(cancellationToken);

                coverPhotoVersionByProject = coverPhotos
                    .GroupBy(p => p.ProjectId)
                    .ToDictionary(g => g.Key, g => (int?)g.First().Version);
            }

            // SECTION: Cost facts (batched read to avoid N+1)
            var pncFacts = await _db.ProjectPncFacts
                .AsNoTracking()
                .Where(x => projectIds.Contains(x.ProjectId))
                .Select(x => new { x.ProjectId, x.PncCost, x.CreatedOnUtc })
                .ToListAsync(cancellationToken);

            var l1Facts = await _db.ProjectCommercialFacts
                .AsNoTracking()
                .Where(x => projectIds.Contains(x.ProjectId))
                .Select(x => new { x.ProjectId, x.L1Cost, x.CreatedOnUtc })
                .ToListAsync(cancellationToken);

            var aonFacts = await _db.ProjectAonFacts
                .AsNoTracking()
                .Where(x => projectIds.Contains(x.ProjectId))
                .Select(x => new { x.ProjectId, x.AonCost, x.CreatedOnUtc })
                .ToListAsync(cancellationToken);

            var ipaFacts = await _db.ProjectIpaFacts
                .AsNoTracking()
                .Where(x => projectIds.Contains(x.ProjectId))
                .Select(x => new { x.ProjectId, x.IpaCost, x.CreatedOnUtc })
                .ToListAsync(cancellationToken);

            var pncByProject = pncFacts
                .GroupBy(x => x.ProjectId)
                .ToDictionary(
                    g => g.Key,
                    g => (decimal?)g.OrderByDescending(x => x.CreatedOnUtc).First().PncCost);

            var l1ByProject = l1Facts
                .GroupBy(x => x.ProjectId)
                .ToDictionary(
                    g => g.Key,
                    g => (decimal?)g.OrderByDescending(x => x.CreatedOnUtc).First().L1Cost);

            var aonByProject = aonFacts
                .GroupBy(x => x.ProjectId)
                .ToDictionary(
                    g => g.Key,
                    g => (decimal?)g.OrderByDescending(x => x.CreatedOnUtc).First().AonCost);

            var ipaByProject = ipaFacts
                .GroupBy(x => x.ProjectId)
                .ToDictionary(
                    g => g.Key,
                    g => (decimal?)g.OrderByDescending(x => x.CreatedOnUtc).First().IpaCost);

            // 1) stages for the selected projects
            var allStages = await _db.ProjectStages
                .AsNoTracking()
                .Where(s => projectIds.Contains(s.ProjectId))
                .ToListAsync(cancellationToken);

            // 2) remarks for the selected projects
            // we fetch all and split in memory – easy and still OK for this screen
            var nowUtc = DateTime.UtcNow;
            var tenDaysAgoUtc = nowUtc.AddDays(-10);

            var allRemarks = await _db.Remarks
                .AsNoTracking()
                .Where(r =>
                    projectIds.Contains(r.ProjectId) &&
                    !r.IsDeleted &&
                    r.Scope == RemarkScope.General)
                .OrderByDescending(r => r.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var result = new List<OngoingProjectRowDto>(projects.Count);

            foreach (var proj in projects)
            {
                // SECTION: Resolve cover photo version fallback
                int? resolvedCoverPhotoVersion = null;
                if (proj.CoverPhotoId.HasValue)
                {
                    resolvedCoverPhotoVersion = coverPhotoVersionByProject.TryGetValue(proj.Id, out var version)
                        ? version
                        : proj.CoverPhotoVersion;
                }

                // SECTION: Cost selection (PNC -> L1 -> AON -> IPA)
                decimal? pnc = pncByProject.TryGetValue(proj.Id, out var pncVal) ? pncVal : null;
                decimal? l1 = l1ByProject.TryGetValue(proj.Id, out var l1Val) ? l1Val : null;
                decimal? aon = aonByProject.TryGetValue(proj.Id, out var aonVal) ? aonVal : null;
                decimal? ipa = ipaByProject.TryGetValue(proj.Id, out var ipaVal) ? ipaVal : null;

                decimal? chosenInInr = null;
                string? label = null;

                if (pnc.HasValue)
                {
                    chosenInInr = pnc;
                    label = "PNC Cost";
                }
                else if (l1.HasValue)
                {
                    chosenInInr = l1;
                    label = "L1 Cost";
                }
                else if (aon.HasValue)
                {
                    chosenInInr = aon;
                    label = "AON Cost";
                }
                else if (ipa.HasValue)
                {
                    chosenInInr = ipa;
                    label = "IPA Cost";
                }

                // SECTION: Cost formatting (full + short)
                string costFull = "NA";
                string costShort = "NA";
                if (chosenInInr.HasValue && label != null)
                {
                    var lakhs = chosenInInr.Value / 100000m;
                    var lakhsText = lakhs.ToString("0.##", CultureInfo.InvariantCulture);
                    var shortLabel = label.Replace(" Cost", string.Empty, StringComparison.Ordinal);
                    costFull = $"{lakhsText} lakhs ({label})";
                    costShort = $"{lakhsText} ({shortLabel})";
                }

                // stages grouped by code
                var stagesForProject = allStages
                    .Where(s => s.ProjectId == proj.Id)
                    .ToDictionary(s => s.StageCode, s => s);

                // SECTION: Workflow-aware stage ordering
                var stageCodes = ProcurementWorkflow.StageCodesFor(proj.WorkflowVersion);
                var stageDtos = new List<OngoingProjectStageDto>(stageCodes.Length);

                int? inProgressIndex = null;
                int lastCompletedIndex = -1;

                for (var i = 0; i < stageCodes.Length; i++)
                {
                    var code = stageCodes[i];
                    stagesForProject.TryGetValue(code, out var stageRow);

                    var status = StageStatus.NotStarted;
                    DateOnly? actualStart = null;
                    DateOnly? actualCompleted = null;
                    DateOnly? plannedDue = null;
                    int? actualDurationDays = null;
                    var isDataMissing = false;

                    if (stageRow != null)
                    {
                        status = stageRow.Status;
                        actualStart = stageRow.ActualStart;
                        actualCompleted = stageRow.CompletedOn;
                        plannedDue = stageRow.PlannedDue;

                        // SECTION: Duration calculation for timeline display
                        if (actualStart.HasValue && actualCompleted.HasValue)
                        {
                            var rawDuration = actualCompleted.Value.DayNumber - actualStart.Value.DayNumber + 1;
                            actualDurationDays = rawDuration > 0 ? rawDuration : null;
                        }
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
                        Name = _workflowStageMetadataProvider.GetDisplayName(proj.WorkflowVersion, code),
                        Status = status,
                        ActualStart = actualStart,
                        ActualCompletedOn = actualCompleted,
                        PlannedDue = plannedDue,
                        ActualDurationDays = actualDurationDays,
                        IsDataMissing = isDataMissing,
                        IsCurrent = false
                    });
                }

                // pick "current" stage
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

                // SECTION: Timeline-independent stage lookups (full list)
                var ipaDate = ResolveStageMilestoneDate(stageDtos, "IPA");
                var aonDate = ResolveStageMilestoneDate(stageDtos, "AON");
                var presentStagePdc = stageDtos[currentIndex].PlannedDue;

                var stageSnapshots = stageDtos
                    .Select((stage, idx) => new ProjectStageStatusSnapshot(
                        stage.Code,
                        stage.Status,
                        idx,
                        stage.ActualStart,
                        stage.ActualCompletedOn))
                    .ToList();

                var presentStage = PresentStageHelper.ComputePresentStageAndAge(
                    stageSnapshots,
                    _workflowStageMetadataProvider,
                    proj.WorkflowVersion);

                string? lastCompletedName = null;
                DateOnly? lastCompletedDate = null;
                if (lastCompletedIndex >= 0)
                {
                    lastCompletedName = stageDtos[lastCompletedIndex].Name;
                    lastCompletedDate = stageDtos[lastCompletedIndex].ActualCompletedOn;
                }

                // remarks for this project
                var remarksForProject = allRemarks
                    .Where(r => r.ProjectId == proj.Id)
                    .ToList();

                // last 10 INTERNAL in last 10 days
                var recentInternal = remarksForProject
                    .Where(r =>
                        r.Type == RemarkType.Internal &&
                        r.CreatedAtUtc >= tenDaysAgoUtc)
                    .OrderByDescending(r => r.CreatedAtUtc)
                    .Take(10)
                    .Select(r => new OngoingProjectRemarkDto
                    {
                        CreatedAtUtc = r.CreatedAtUtc,
                        Text = r.Body,
                        ActorRole = r.AuthorRole
                    })
                    .ToArray();

                // SECTION: Latest external remark (if any)
                var latestExternal = remarksForProject
                    .Where(r => r.Type == RemarkType.External)
                    .OrderByDescending(r => r.CreatedAtUtc)
                    .FirstOrDefault();

                OngoingProjectExternalRemarkDto? latestExternalDto = null;
                if (latestExternal != null)
                {
                    latestExternalDto = new OngoingProjectExternalRemarkDto
                    {
                        Id = latestExternal.Id,
                        Body = latestExternal.Body,
                        EventDate = latestExternal.EventDate,
                        Scope = latestExternal.Scope,
                        RowVersion = latestExternal.RowVersion is { Length: > 0 } rowVersion
                            ? Convert.ToBase64String(rowVersion)
                            : string.Empty
                    };
                }

                result.Add(new OngoingProjectRowDto
                {
                    ProjectId = proj.Id,
                    ProjectName = proj.Name,
                    ProjectCategoryId = proj.CategoryId,
                    ProjectCategoryName = proj.CategoryName,
                    LeadPoUserId = proj.LeadPoUserId,
                    LeadPoName = proj.LeadPoName,
                    CurrentStageCode = stageDtos[currentIndex].Code,
                    CurrentStageName = stageDtos[currentIndex].Name,
                    LastCompletedStageName = lastCompletedName,
                    LastCompletedStageDate = lastCompletedDate,
                    PresentStage = presentStage,
                    IpaDate = ipaDate,
                    AonDate = aonDate,
                    PresentStagePdc = presentStagePdc,
                    Stages = stageDtos,
                    RecentInternalRemarks = recentInternal,
                    LatestExternalRemark = latestExternalDto,
                    CostLakhsText = costFull,
                    CostLakhsFull = costFull,
                    CostLakhsShort = costShort,
                    CoverPhotoId = proj.CoverPhotoId,
                    CoverPhotoVersion = resolvedCoverPhotoVersion
                });
            }

            return result;
        }

        /// <summary>
        /// Build officer dropdown from all active projects that have a LeadPoUser.
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
                    Name = p.LeadPoUser != null
                        ? (p.LeadPoUser.FullName ?? p.LeadPoUser.UserName)
                        : p.LeadPoUserId
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

        // -------------------- Category scope helpers --------------------
        private async Task<int[]> GetCategoryScopeIdsAsync(int rootCategoryId, CancellationToken cancellationToken)
        {
            var categories = await _db.ProjectCategories
                .AsNoTracking()
                .Select(c => new { c.Id, c.ParentId })
                .ToListAsync(cancellationToken);

            var childrenByParent = categories
                .Where(c => c.ParentId.HasValue)
                .GroupBy(c => c.ParentId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

            var collectedIds = new List<int> { rootCategoryId };
            var collectedSet = new HashSet<int>(collectedIds);
            var toVisit = new Queue<int>();
            toVisit.Enqueue(rootCategoryId);

            while (toVisit.Count > 0)
            {
                var current = toVisit.Dequeue();

                if (!childrenByParent.TryGetValue(current, out var childIds))
                {
                    continue;
                }

                foreach (var childId in childIds)
                {
                    if (!collectedSet.Add(childId))
                    {
                        continue;
                    }

                    collectedIds.Add(childId);
                    toVisit.Enqueue(childId);
                }
            }

            return collectedIds.ToArray();
        }

        // SECTION: Stage milestone helpers
        private static DateOnly? ResolveStageMilestoneDate(
            IEnumerable<OngoingProjectStageDto> stages,
            string stageCode)
        {
            var stage = stages.FirstOrDefault(s =>
                string.Equals(s.Code, stageCode, StringComparison.OrdinalIgnoreCase));

            return stage?.ActualCompletedOn;
        }
    }

    public sealed class OngoingProjectRowDto
    {
        public int ProjectId { get; init; }
        public string ProjectName { get; init; } = "";
        public int? ProjectCategoryId { get; init; }
        public string? ProjectCategoryName { get; init; }

        public string? LeadPoUserId { get; init; }
        public string? LeadPoName { get; init; }

        public string CurrentStageCode { get; init; } = "";
        public string? CurrentStageName { get; init; }
        public string? LastCompletedStageName { get; init; }
        public DateOnly? LastCompletedStageDate { get; init; }

        public PresentStageSnapshot PresentStage { get; init; } = PresentStageSnapshot.Empty;

        public DateOnly? IpaDate { get; init; }
        public DateOnly? AonDate { get; init; }
        public DateOnly? PresentStagePdc { get; init; }

        public IReadOnlyList<OngoingProjectStageDto> Stages { get; init; } = Array.Empty<OngoingProjectStageDto>();

        public IReadOnlyList<OngoingProjectRemarkDto> RecentInternalRemarks { get; init; } = Array.Empty<OngoingProjectRemarkDto>();
        public OngoingProjectExternalRemarkDto? LatestExternalRemark { get; init; }

        public string CostLakhsText { get; init; } = "NA";
        public string CostLakhsFull { get; init; } = "NA";
        public string CostLakhsShort { get; init; } = "NA";

        public int? CoverPhotoId { get; init; }
        public int? CoverPhotoVersion { get; init; }
    }

    // SECTION: Latest external remark details for inline editing
    public sealed class OngoingProjectExternalRemarkDto
    {
        public int Id { get; init; }
        public string Body { get; init; } = "";
        public DateOnly EventDate { get; init; }
        public RemarkScope Scope { get; init; } = RemarkScope.General;
        public string RowVersion { get; init; } = "";
    }

    public sealed class OngoingProjectStageDto
    {
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public StageStatus Status { get; init; }
        public DateOnly? ActualStart { get; init; }
        public DateOnly? ActualCompletedOn { get; init; }
        public DateOnly? PlannedDue { get; init; }
        public int? ActualDurationDays { get; init; }
        public bool IsDataMissing { get; init; }
        public bool IsCurrent { get; set; }
    }

    public sealed class OngoingProjectRemarkDto
    {
        public DateTime CreatedAtUtc { get; init; }
        public string Text { get; init; } = "";
        public RemarkActorRole ActorRole { get; init; }
    }
}
