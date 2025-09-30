using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using ProjectManagement.Data;
using ProjectManagement.Features.Backfill;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using ProjectManagement.Utilities;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects
{
    [Authorize]
    public class OverviewModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly ProjectProcurementReadService _procureRead;
        private readonly ProjectTimelineReadService _timelineRead;
        private readonly UserManager<ApplicationUser> _users;
        private readonly PlanReadService _planRead;
        private readonly ILogger<OverviewModel> _logger;
        private readonly IClock _clock;

        public PlanCompareService PlanCompare { get; }

        public OverviewModel(ApplicationDbContext db, ProjectProcurementReadService procureRead, ProjectTimelineReadService timelineRead, UserManager<ApplicationUser> users, PlanReadService planRead, PlanCompareService planCompare, ILogger<OverviewModel> logger, IClock clock)
        {
            _db = db;
            _procureRead = procureRead;
            _timelineRead = timelineRead;
            _users = users;
            _planRead = planRead;
            PlanCompare = planCompare;
            _logger = logger;
            _clock = clock;
        }

        public Project Project { get; private set; } = default!;
        public IList<ProjectStage> Stages { get; private set; } = new List<ProjectStage>();
        public IReadOnlyList<ProjectCategory> CategoryPath { get; private set; } = Array.Empty<ProjectCategory>();
        public ProcurementAtAGlanceVm Procurement { get; private set; } = default!;
        public ProcurementEditVm ProcurementEdit { get; private set; } = default!;
        public AssignRolesVm AssignRoles { get; private set; } = default!;
        public TimelineVm Timeline { get; private set; } = default!;
        public PlanEditorVm PlanEdit { get; private set; } = default!;
        public BackfillViewModel Backfill { get; private set; } = BackfillViewModel.Empty;
        public bool HasBackfill { get; private set; }
        public bool RequiresPlanApproval { get; private set; }
        public string? CurrentUserId { get; private set; }
        public ProjectMetaChangeRequestVm? MetaChangeRequest { get; private set; }
        public IReadOnlyList<ProjectPhoto> Photos { get; private set; } = Array.Empty<ProjectPhoto>();
        public ProjectPhoto? CoverPhoto { get; private set; }
        public int? CoverPhotoVersion { get; private set; }
        public string? CoverPhotoUrl { get; private set; }

        public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
        {
            CurrentUserId = _users.GetUserId(User);

            var project = await _db.Projects
                .Include(p => p.Category)
                .Include(p => p.HodUser)
                .Include(p => p.LeadPoUser)
                .Include(p => p.PlanApprovedByUser)
                .Include(p => p.SponsoringUnit)
                .Include(p => p.SponsoringLineDirectorate)
                .Include(p => p.Photos)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (project is null)
            {
                return NotFound();
            }

            Project = project;

            Photos = project.Photos
                .OrderBy(p => p.Ordinal)
                .ThenBy(p => p.Id)
                .ToList();

            if (project.CoverPhotoId.HasValue)
            {
                CoverPhoto = Photos.FirstOrDefault(p => p.Id == project.CoverPhotoId.Value);
                CoverPhotoVersion = CoverPhoto?.Version ?? project.CoverPhotoVersion;
                if (CoverPhoto is not null)
                {
                    CoverPhotoUrl = Url.Page("/Projects/Photos/View", new
                    {
                        id = project.Id,
                        photoId = CoverPhoto.Id,
                        size = "md",
                        v = CoverPhotoVersion
                    });
                }
            }

            var connectionHash = ConnectionStringHasher.Hash(_db.Database.GetConnectionString());

            var projectStages = await _db.ProjectStages
                .Where(s => s.ProjectId == id)
                .ToListAsync(ct);

            Stages = projectStages
                .OrderBy(s => StageOrder(s.StageCode))
                .ThenBy(s => s.StageCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var stageLookup = projectStages
                .Where(s => s.StageCode is not null)
                .ToDictionary(s => s.StageCode!, s => s.Status, StringComparer.OrdinalIgnoreCase);

            bool Completed(string code) => stageLookup.TryGetValue(code, out var status) && status == StageStatus.Completed;

            if (project.CategoryId.HasValue)
            {
                CategoryPath = await BuildCategoryPathAsync(project.CategoryId.Value, ct);
            }

            Procurement = await _procureRead.GetAsync(id, ct);
            _logger.LogInformation(
                "Overview building timeline. ProjectId={ProjectId}, ConnHash={ConnHash}",
                id,
                connectionHash);
            Timeline = await _timelineRead.GetAsync(id, ct);
            PlanEdit = await _planRead.GetAsync(id, CurrentUserId, ct);
            HasBackfill = Timeline.HasBackfill;
            Backfill = BuildBackfillViewModel(id);
            RequiresPlanApproval = Timeline.PlanPendingApproval;

            ProcurementEdit = new ProcurementEditVm
            {
                Input = new ProcurementEditInput
                {
                    ProjectId = id,
                    IpaCost = Procurement.IpaCost,
                    AonCost = Procurement.AonCost,
                    BenchmarkCost = Procurement.BenchmarkCost,
                    L1Cost = Procurement.L1Cost,
                    PncCost = Procurement.PncCost,
                    SupplyOrderDate = Procurement.SupplyOrderDate
                },
                CanEditIpaCost = Completed(ProcurementStageRules.StageForIpaCost),
                CanEditAonCost = Completed(ProcurementStageRules.StageForAonCost),
                CanEditBenchmarkCost = Completed(ProcurementStageRules.StageForBenchmarkCost),
                CanEditL1Cost = Completed(ProcurementStageRules.StageForL1Cost),
                CanEditPncCost = Completed(ProcurementStageRules.StageForPncCost),
                CanEditSupplyOrderDate = Completed(ProcurementStageRules.StageForSupplyOrder)
            };

            AssignRoles = await BuildAssignRolesVmAsync(project);

            var draftState = PlanEdit.State ?? new PlanEditorStateVm();
            var draftExists = draftState.HasMyDraft || draftState.HasPendingSubmission;
            ViewData["DiagDraftExists"] = draftExists ? "1" : "0";
            _logger.LogInformation(
                "Overview load complete. ProjectId={ProjectId}, ConnHash={ConnHash}, DraftExists={DraftExists}",
                id,
                connectionHash,
                draftExists);

            var pendingMetaRequest = await _db.ProjectMetaChangeRequests
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.ProjectId == id && r.DecisionStatus == ProjectMetaDecisionStatuses.Pending, ct);

            if (pendingMetaRequest is not null)
            {
                MetaChangeRequest = await BuildMetaChangeRequestVmAsync(project, pendingMetaRequest, ct);
            }

            return Page();
        }

        private BackfillViewModel BuildBackfillViewModel(int projectId)
        {
            if (Timeline is null)
            {
                return BackfillViewModel.Empty;
            }

            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, TimeZoneHelper.GetIst()).Date);

            var stages = Timeline.Items
                .Where(item => item.RequiresBackfill)
                .Select(item => new BackfillStageViewModel
                {
                    StageCode = item.Code,
                    StageName = item.Name,
                    ActualStart = item.ActualStart,
                    CompletedOn = item.CompletedOn,
                    IsAutoCompleted = item.IsAutoCompleted,
                    AutoCompletedFromCode = item.AutoCompletedFromCode
                })
                .ToArray();

            return new BackfillViewModel
            {
                ProjectId = projectId,
                Today = today,
                Stages = stages
            };
        }

        private async Task<ProjectMetaChangeRequestVm?> BuildMetaChangeRequestVmAsync(Project project, ProjectMetaChangeRequest request, CancellationToken ct)
        {
            ProjectMetaChangeRequestPayload? payload;

            try
            {
                payload = JsonSerializer.Deserialize<ProjectMetaChangeRequestPayload>(request.Payload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse meta change payload for request {RequestId}.", request.Id);
                return null;
            }

            if (payload is null)
            {
                _logger.LogWarning("Meta change payload for request {RequestId} was null.", request.Id);
                return null;
            }

            static string Format(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

            var originalNameDisplay = Format(request.OriginalName);
            var originalDescriptionDisplay = Format(request.OriginalDescription);
            var originalCaseFileDisplay = Format(request.OriginalCaseFileNumber);
            var proposedNameRaw = string.IsNullOrWhiteSpace(payload.Name) ? project.Name : payload.Name.Trim();
            var proposedNameDisplay = Format(proposedNameRaw);
            var proposedDescription = string.IsNullOrWhiteSpace(payload.Description) ? null : payload.Description.Trim();
            var proposedDescriptionDisplay = Format(proposedDescription);
            var proposedCaseFileNumber = string.IsNullOrWhiteSpace(payload.CaseFileNumber) ? null : payload.CaseFileNumber.Trim();
            var proposedCaseFileDisplay = Format(proposedCaseFileNumber);
            var proposedCategoryId = payload.CategoryId;
            var proposedUnitId = payload.SponsoringUnitId;
            var proposedLineDirectorateId = payload.SponsoringLineDirectorateId;

            var currentUnitDisplay = Format(project.SponsoringUnit?.Name);
            var currentLineDirectorateDisplay = Format(project.SponsoringLineDirectorate?.Name);

            var originalUnitName = request.OriginalSponsoringUnitId.HasValue
                ? await _db.SponsoringUnits.AsNoTracking()
                    .Where(u => u.Id == request.OriginalSponsoringUnitId.Value)
                    .Select(u => u.Name)
                    .FirstOrDefaultAsync(ct)
                : null;
            var originalUnitDisplay = request.OriginalSponsoringUnitId.HasValue
                ? (string.IsNullOrWhiteSpace(originalUnitName) ? "(inactive)" : Format(originalUnitName))
                : "—";

            var originalLineName = request.OriginalSponsoringLineDirectorateId.HasValue
                ? await _db.LineDirectorates.AsNoTracking()
                    .Where(l => l.Id == request.OriginalSponsoringLineDirectorateId.Value)
                    .Select(l => l.Name)
                    .FirstOrDefaultAsync(ct)
                : null;
            var originalLineDisplay = request.OriginalSponsoringLineDirectorateId.HasValue
                ? (string.IsNullOrWhiteSpace(originalLineName) ? "(inactive)" : Format(originalLineName))
                : "—";

            string proposedUnitDisplay;
            if (proposedUnitId.HasValue)
            {
                var proposedUnitName = await _db.SponsoringUnits.AsNoTracking()
                    .Where(u => u.Id == proposedUnitId.Value)
                    .Select(u => u.Name)
                    .FirstOrDefaultAsync(ct);
                proposedUnitDisplay = string.IsNullOrWhiteSpace(proposedUnitName) ? "(inactive)" : Format(proposedUnitName);
            }
            else
            {
                proposedUnitDisplay = "—";
            }

            string proposedLineDisplay;
            if (proposedLineDirectorateId.HasValue)
            {
                var proposedLineName = await _db.LineDirectorates.AsNoTracking()
                    .Where(l => l.Id == proposedLineDirectorateId.Value)
                    .Select(l => l.Name)
                    .FirstOrDefaultAsync(ct);
                proposedLineDisplay = string.IsNullOrWhiteSpace(proposedLineName) ? "(inactive)" : Format(proposedLineName);
            }
            else
            {
                proposedLineDisplay = "—";
            }

            var originalCategoryDisplay = "—";
            if (request.OriginalCategoryId.HasValue)
            {
                var originalPath = await BuildCategoryPathAsync(request.OriginalCategoryId.Value, ct);
                if (originalPath.Any())
                {
                    originalCategoryDisplay = string.Join(" › ", originalPath.Select(c => c.Name));
                }
            }

            var currentCategoryDisplay = CategoryPath.Any()
                ? string.Join(" › ", CategoryPath.Select(c => c.Name))
                : "—";

            string proposedCategoryDisplay = "—";
            if (proposedCategoryId.HasValue)
            {
                var proposedPath = await BuildCategoryPathAsync(proposedCategoryId.Value, ct);
                if (proposedPath.Any())
                {
                    proposedCategoryDisplay = string.Join(" › ", proposedPath.Select(c => c.Name));
                }
            }

            var requestedBy = await GetDisplayNameAsync(request.RequestedByUserId);

            var driftFields = ProjectMetaChangeDriftDetector.Detect(project, request);
            var drift = new List<ProjectMetaChangeDriftVm>();

            foreach (var field in driftFields)
            {
                switch (field)
                {
                    case ProjectMetaChangeDriftFields.Name:
                        drift.Add(new ProjectMetaChangeDriftVm("Name", originalNameDisplay, Format(project.Name), false));
                        break;
                    case ProjectMetaChangeDriftFields.Description:
                        drift.Add(new ProjectMetaChangeDriftVm("Description", originalDescriptionDisplay, Format(project.Description), false));
                        break;
                    case ProjectMetaChangeDriftFields.CaseFileNumber:
                        drift.Add(new ProjectMetaChangeDriftVm("Case file number", originalCaseFileDisplay, Format(project.CaseFileNumber), false));
                        break;
                    case ProjectMetaChangeDriftFields.Category:
                        drift.Add(new ProjectMetaChangeDriftVm("Category", originalCategoryDisplay, currentCategoryDisplay, false));
                        break;
                    case ProjectMetaChangeDriftFields.SponsoringUnit:
                        drift.Add(new ProjectMetaChangeDriftVm("Sponsoring Unit", originalUnitDisplay, currentUnitDisplay, false));
                        break;
                    case ProjectMetaChangeDriftFields.SponsoringLineDirectorate:
                        drift.Add(new ProjectMetaChangeDriftVm("Sponsoring Line Dte", originalLineDisplay, currentLineDirectorateDisplay, false));
                        break;
                    case ProjectMetaChangeDriftFields.ProjectRecord:
                        drift.Add(new ProjectMetaChangeDriftVm("Project record", "Submission snapshot", "Updated after submission", true));
                        break;
                }
            }

            return new ProjectMetaChangeRequestVm
            {
                RequestId = request.Id,
                RequestedBy = requestedBy,
                RequestedByUserId = request.RequestedByUserId,
                RequestedOnUtc = request.RequestedOnUtc,
                RequestNote = request.RequestNote,
                OriginalName = originalNameDisplay,
                OriginalDescription = originalDescriptionDisplay,
                OriginalCaseFileNumber = originalCaseFileDisplay,
                OriginalCategory = originalCategoryDisplay,
                Name = new ProjectMetaChangeFieldVm(project.Name, proposedNameDisplay, !string.Equals(project.Name, proposedNameRaw, StringComparison.Ordinal)),
                Description = new ProjectMetaChangeFieldVm(Format(project.Description), proposedDescriptionDisplay, !string.Equals(project.Description ?? string.Empty, proposedDescription ?? string.Empty, StringComparison.Ordinal)),
                CaseFileNumber = new ProjectMetaChangeFieldVm(Format(project.CaseFileNumber), proposedCaseFileDisplay, !string.Equals(project.CaseFileNumber ?? string.Empty, proposedCaseFileNumber ?? string.Empty, StringComparison.Ordinal)),
                Category = new ProjectMetaChangeFieldVm(currentCategoryDisplay, proposedCategoryDisplay, project.CategoryId != proposedCategoryId),
                SponsoringUnit = new ProjectMetaChangeFieldVm(currentUnitDisplay, proposedUnitDisplay, project.SponsoringUnitId != proposedUnitId),
                SponsoringLineDirectorate = new ProjectMetaChangeFieldVm(currentLineDirectorateDisplay, proposedLineDisplay, project.SponsoringLineDirectorateId != proposedLineDirectorateId),
                HasDrift = drift.Count > 0,
                Drift = drift
            };
        }

        private async Task<string> GetDisplayNameAsync(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return "Unknown";
            }

            var user = await _users.FindByIdAsync(userId);
            if (user is null)
            {
                return "Unknown";
            }

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

        private async Task<AssignRolesVm> BuildAssignRolesVmAsync(Project project)
        {
            var hodUsers = await _users.GetUsersInRoleAsync("HoD");
            var poUsers = await _users.GetUsersInRoleAsync("Project Officer");

            static string DisplayName(ApplicationUser user)
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

            var hodOptions = hodUsers
                .Select(user => (user.Id, Name: DisplayName(user)))
                .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var poOptions = poUsers
                .Select(user => (user.Id, Name: DisplayName(user)))
                .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new AssignRolesVm
            {
                ProjectId = project.Id,
                RowVersion = project.RowVersion,
                HodUserId = project.HodUserId,
                PoUserId = project.LeadPoUserId,
                HodOptions = hodOptions,
                PoOptions = poOptions
            };
        }

        private async Task<IReadOnlyList<ProjectCategory>> BuildCategoryPathAsync(int categoryId, CancellationToken ct)
        {
            var path = new List<ProjectCategory>();
            var visited = new HashSet<int>();
            var currentId = categoryId;

            while (true)
            {
                if (!visited.Add(currentId))
                {
                    break;
                }

                var category = await _db.ProjectCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == currentId, ct);
                if (category is null)
                {
                    break;
                }

                path.Insert(0, category);

                if (category.ParentId is null)
                {
                    break;
                }

                currentId = category.ParentId.Value;
            }

            return path;
        }

        private static int StageOrder(string? stageCode)
        {
            if (stageCode is null)
            {
                return int.MaxValue;
            }

            var index = Array.IndexOf(StageCodes.All, stageCode);
            return index >= 0 ? index : int.MaxValue;
        }
    }
}
