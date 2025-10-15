using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
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
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Remarks;
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
        private readonly ProjectRemarksPanelService _remarksPanelService;
        private readonly ProjectLifecycleService _lifecycleService;
        private readonly ProjectMediaAggregator _mediaAggregator;

        public PlanCompareService PlanCompare { get; }

        public OverviewModel(ApplicationDbContext db, ProjectProcurementReadService procureRead, ProjectTimelineReadService timelineRead, UserManager<ApplicationUser> users, PlanReadService planRead, PlanCompareService planCompare, ILogger<OverviewModel> logger, IClock clock, ProjectRemarksPanelService remarksPanelService, ProjectLifecycleService lifecycleService, ProjectMediaAggregator mediaAggregator)
        {
            _db = db;
            _procureRead = procureRead;
            _timelineRead = timelineRead;
            _users = users;
            _planRead = planRead;
            PlanCompare = planCompare;
            _logger = logger;
            _clock = clock;
            _remarksPanelService = remarksPanelService;
            _lifecycleService = lifecycleService;
            _mediaAggregator = mediaAggregator;
        }

        public Project Project { get; private set; } = default!;
        public IList<ProjectStage> Stages { get; private set; } = new List<ProjectStage>();
        public IReadOnlyList<ProjectCategory> CategoryPath { get; private set; } = Array.Empty<ProjectCategory>();
        public IReadOnlyList<TechnicalCategory> TechnicalCategoryPath { get; private set; } = Array.Empty<TechnicalCategory>();
        public ProcurementAtAGlanceVm Procurement { get; private set; } = default!;
        public ProcurementEditVm ProcurementEdit { get; private set; } = default!;
        public AssignRolesVm AssignRoles { get; private set; } = default!;
        public TimelineVm Timeline { get; private set; } = default!;
        public PlanEditorVm PlanEdit { get; private set; } = default!;
        public BackfillViewModel Backfill { get; private set; } = BackfillViewModel.Empty;
        public ProjectRemarksPanelViewModel RemarksPanel { get; private set; } = ProjectRemarksPanelViewModel.Empty;
        public bool HasBackfill { get; private set; }
        public bool RequiresPlanApproval { get; private set; }
        public string? CurrentUserId { get; private set; }
        public ProjectMetaChangeRequestVm? MetaChangeRequest { get; private set; }
        public IReadOnlyList<ProjectPhoto> Photos { get; private set; } = Array.Empty<ProjectPhoto>();
        public IReadOnlyList<ProjectVideo> Videos { get; private set; } = Array.Empty<ProjectVideo>();
        public ProjectPhoto? CoverPhoto { get; private set; }
        public int? CoverPhotoVersion { get; private set; }
        public string? CoverPhotoUrl { get; private set; }
        public ProjectVideo? FeaturedVideo { get; private set; }
        public int? FeaturedVideoVersion { get; private set; }
        public string? FeaturedVideoUrl { get; private set; }

        public ProjectRolesViewModel Roles { get; private set; } = ProjectRolesViewModel.Empty;
        public ProjectLifecycleSummaryViewModel LifecycleSummary { get; private set; } = ProjectLifecycleSummaryViewModel.Empty;
        public ProjectLifecycleActionsViewModel LifecycleActions { get; private set; } = ProjectLifecycleActionsViewModel.Empty;
        public ProjectMediaSummaryViewModel MediaSummary { get; private set; } = ProjectMediaSummaryViewModel.Empty;
        public ProjectDocumentSummaryViewModel DocumentSummary { get; private set; } = ProjectDocumentSummaryViewModel.Empty;
        public ProjectRemarkSummaryViewModel RemarkSummary { get; private set; } = ProjectRemarkSummaryViewModel.Empty;
        public ProjectTotSummaryViewModel TotSummary { get; private set; } = ProjectTotSummaryViewModel.Empty;
        public bool CanManageTot { get; private set; }
        public ProjectMediaCollectionViewModel MediaCollections { get; private set; } = ProjectMediaCollectionViewModel.Empty;
        public IReadOnlyCollection<int> AvailableMediaTotIds { get; private set; } = Array.Empty<int>();
        public bool CanUploadDocuments { get; private set; }
        public bool CanViewDocumentRecycleBin { get; private set; }
        public bool CanManagePhotos { get; private set; }
        public bool CanManageVideos { get; private set; }

        [BindProperty(SupportsGet = true)]
        public string? DocumentStageFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? DocumentStatusFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public int DocumentPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public int? MediaTotId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? MediaTab { get; set; }

        public ProjectDocumentListViewModel DocumentList { get; private set; } = ProjectDocumentListViewModel.Empty;

        public IReadOnlyList<ProjectDocumentPendingRequestViewModel> DocumentPendingRequests { get; private set; } = Array.Empty<ProjectDocumentPendingRequestViewModel>();

        public bool IsDocumentApprover { get; private set; }

        public int DocumentPendingRequestCount { get; private set; }

        [BindProperty]
        public CompleteLifecycleInput CompleteProjectInput { get; set; } = new();

        [BindProperty]
        public EndorseLifecycleInput EndorseCompletionInput { get; set; } = new();

        [BindProperty]
        public CancelLifecycleInput CancelProjectInput { get; set; } = new();

        public sealed class CompleteLifecycleInput
        {
            public int ProjectId { get; set; }

            public int? CompletedYear { get; set; }
        }

        public sealed class EndorseLifecycleInput
        {
            public int ProjectId { get; set; }

            public DateOnly? CompletedOn { get; set; }
        }

        public sealed class CancelLifecycleInput
        {
            public int ProjectId { get; set; }

            public DateOnly? CancelledOn { get; set; }

            public string? Reason { get; set; }
        }

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
                .Include(p => p.TechnicalCategory)
                .Include(p => p.Photos)
                .Include(p => p.Videos)
                .Include(p => p.Tot)
                    .ThenInclude(t => t.LastApprovedByUser)
                .Include(p => p.TotRequest)
                    .ThenInclude(r => r.SubmittedByUser)
                .Include(p => p.TotRequest)
                    .ThenInclude(r => r.DecidedByUser)
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

            Videos = project.Videos
                .OrderBy(v => v.Ordinal)
                .ThenBy(v => v.Id)
                .ToList();

            var availableTotIds = Photos
                .Where(p => p.TotId.HasValue)
                .Select(p => p.TotId!.Value)
                .ToHashSet();

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

            if (project.FeaturedVideoId.HasValue)
            {
                FeaturedVideo = Videos.FirstOrDefault(v => v.Id == project.FeaturedVideoId.Value);
                FeaturedVideoVersion = FeaturedVideo?.Version ?? project.FeaturedVideoVersion;
                if (FeaturedVideo is not null)
                {
                    FeaturedVideoUrl = Url.Page("/Projects/Videos/Stream", new
                    {
                        id = project.Id,
                        videoId = FeaturedVideo.Id,
                        v = FeaturedVideoVersion
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

            var isAdmin = User.IsInRole("Admin");
            var isHoD = User.IsInRole("HoD");
            var isProjectOfficer = User.IsInRole("Project Officer");
            var isThisProjectsPo = isProjectOfficer && string.Equals(project.LeadPoUserId, CurrentUserId, StringComparison.Ordinal);
            var isThisProjectsHod = isHoD && string.Equals(project.HodUserId, CurrentUserId, StringComparison.Ordinal);

            CanManagePhotos = isAdmin || isThisProjectsPo || isThisProjectsHod;
            CanManageVideos = CanManagePhotos;
            CanUploadDocuments = isAdmin || isThisProjectsPo || isThisProjectsHod;
            CanViewDocumentRecycleBin = isAdmin;

            Roles = new ProjectRolesViewModel
            {
                IsAdmin = isAdmin,
                IsHoD = isHoD,
                IsProjectOfficer = isProjectOfficer,
                IsAssignedProjectOfficer = isThisProjectsPo,
                IsAssignedHoD = isThisProjectsHod
            };

            CanManageTot = isAdmin || isHoD || isThisProjectsPo || isThisProjectsHod;

            var todayLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow.UtcDateTime, TimeZoneHelper.GetIst()));

            CompleteProjectInput ??= new CompleteLifecycleInput();
            CompleteProjectInput.ProjectId = project.Id;
            CompleteProjectInput.CompletedYear = project.CompletedYear;

            EndorseCompletionInput ??= new EndorseLifecycleInput();
            EndorseCompletionInput.ProjectId = project.Id;
            EndorseCompletionInput.CompletedOn = project.CompletedOn;

            CancelProjectInput ??= new CancelLifecycleInput();
            CancelProjectInput.ProjectId = project.Id;
            CancelProjectInput.CancelledOn = project.CancelledOn ?? todayLocalDate;
            CancelProjectInput.Reason = project.CancelReason;

            LifecycleActions = BuildLifecycleActions(project, isAdmin, isHoD, isThisProjectsHod);

            if (project.CategoryId.HasValue)
            {
                CategoryPath = await BuildCategoryPathAsync(project.CategoryId.Value, ct);
            }

            if (project.TechnicalCategoryId.HasValue)
            {
                TechnicalCategoryPath = await BuildTechnicalCategoryPathAsync(project.TechnicalCategoryId.Value, ct);
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

            await LoadDocumentOverviewAsync(project, isAdmin, isHoD, availableTotIds, ct);

            RemarksPanel = await _remarksPanelService.BuildAsync(project, Stages, User, ct);

            LifecycleSummary = BuildLifecycleSummary(project);
            RemarkSummary = await LoadRemarkSummaryAsync(project.Id, ct);
            TotSummary = BuildTotSummary(project);
            MediaSummary = BuildMediaSummary();

            AvailableMediaTotIds = availableTotIds.ToArray();

            var totFilterLabel = TotSummary.HasTotRecord
                ? string.Format(CultureInfo.InvariantCulture, "Transfer of Technology ({0})", TotSummary.StatusLabel)
                : "Transfer of Technology";

            var videoViewModels = BuildVideoViewModels(project);

            MediaCollections = _mediaAggregator.Build(new ProjectMediaAggregationRequest(
                DocumentList,
                DocumentSummary,
                DocumentPendingRequests,
                IsDocumentApprover,
                CanUploadDocuments,
                CanViewDocumentRecycleBin,
                DocumentPendingRequestCount,
                Photos,
                CoverPhoto,
                CoverPhotoVersion,
                CoverPhotoUrl,
                CanManagePhotos,
                CanManageVideos,
                videoViewModels,
                AvailableMediaTotIds,
                MediaTotId,
                MediaTab,
                totFilterLabel));

            return Page();
        }


        public async Task<IActionResult> OnPostCompleteAsync(int id, CancellationToken ct)
        {
            if (CompleteProjectInput is null || CompleteProjectInput.ProjectId != id)
            {
                return BadRequest();
            }

            var userId = _users.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Forbid();
            }

            var projectInfo = await _db.Projects
                .AsNoTracking()
                .Select(p => new
                {
                    p.Id,
                    p.LifecycleStatus,
                    p.CompletedOn,
                    p.LeadPoUserId,
                    p.HodUserId
                })
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (projectInfo is null)
            {
                return NotFound();
            }

            if (!CanManageLifecycle(userId, projectInfo.HodUserId))
            {
                return Forbid();
            }

            var result = await _lifecycleService.MarkCompletedAsync(id, userId, CompleteProjectInput.CompletedYear, ct);

            if (result.Status == ProjectLifecycleOperationStatus.NotFound)
            {
                return NotFound();
            }

            if (result.IsSuccess)
            {
                var message = projectInfo.LifecycleStatus == ProjectLifecycleStatus.Active
                    ? "Project marked as completed."
                    : "Completion details updated.";
                TempData["Flash"] = message;
            }
            else
            {
                TempData["Error"] = result.ErrorMessage ?? "Unable to update project completion.";
            }

            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostEndorseAsync(int id, CancellationToken ct)
        {
            if (EndorseCompletionInput is null || EndorseCompletionInput.ProjectId != id)
            {
                return BadRequest();
            }

            if (EndorseCompletionInput.CompletedOn is null)
            {
                TempData["Error"] = "Completion date is required to endorse the project.";
                return RedirectToPage(new { id });
            }

            var userId = _users.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Forbid();
            }

            var projectInfo = await _db.Projects
                .AsNoTracking()
                .Select(p => new
                {
                    p.Id,
                    p.LeadPoUserId,
                    p.HodUserId
                })
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (projectInfo is null)
            {
                return NotFound();
            }

            if (!CanManageLifecycle(userId, projectInfo.HodUserId))
            {
                return Forbid();
            }

            var result = await _lifecycleService.EndorseCompletionAsync(id, userId, EndorseCompletionInput.CompletedOn.Value, ct);

            if (result.Status == ProjectLifecycleOperationStatus.NotFound)
            {
                return NotFound();
            }

            if (result.IsSuccess)
            {
                TempData["Flash"] = "Completion date endorsed.";
            }
            else
            {
                TempData["Error"] = result.ErrorMessage ?? "Unable to endorse completion date.";
            }

            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostCancelAsync(int id, CancellationToken ct)
        {
            if (CancelProjectInput is null || CancelProjectInput.ProjectId != id)
            {
                return BadRequest();
            }

            if (CancelProjectInput.CancelledOn is null)
            {
                TempData["Error"] = "Cancellation date is required.";
                return RedirectToPage(new { id });
            }

            var userId = _users.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Forbid();
            }

            var projectInfo = await _db.Projects
                .AsNoTracking()
                .Select(p => new
                {
                    p.Id,
                    p.LifecycleStatus,
                    p.LeadPoUserId,
                    p.HodUserId
                })
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (projectInfo is null)
            {
                return NotFound();
            }

            if (!CanManageLifecycle(userId, projectInfo.HodUserId))
            {
                return Forbid();
            }

            var reason = CancelProjectInput.Reason ?? string.Empty;
            var result = await _lifecycleService.CancelProjectAsync(id, userId, CancelProjectInput.CancelledOn.Value, reason, ct);

            if (result.Status == ProjectLifecycleOperationStatus.NotFound)
            {
                return NotFound();
            }

            if (result.IsSuccess)
            {
                TempData["Flash"] = "Project cancelled.";
            }
            else
            {
                TempData["Error"] = result.ErrorMessage ?? "Unable to cancel project.";
            }

            return RedirectToPage(new { id });
        }



        private async Task LoadDocumentOverviewAsync(Project project, bool isAdmin, bool isHoD, HashSet<int> availableTotIds, CancellationToken ct)
        {
            var isApprover = isAdmin || isHoD;
            IsDocumentApprover = isApprover;

            var normalizedStage = NormalizeDocumentStage(DocumentStageFilter);
            var normalizedStatus = NormalizeDocumentStatus(DocumentStatusFilter, isApprover);

            DocumentStageFilter = normalizedStage;
            DocumentStatusFilter = normalizedStatus;

            var page = DocumentPage <= 0 ? 1 : DocumentPage;

            var documents = await _db.ProjectDocuments
                .AsNoTracking()
                .Where(d => d.ProjectId == project.Id && !d.IsArchived)
                .Include(d => d.Stage)
                .Include(d => d.UploadedByUser)
                .ToListAsync(ct);

            if (!isApprover)
            {
                documents = documents
                    .Where(d => d.Status == ProjectDocumentStatus.Published)
                    .ToList();
            }

            foreach (var document in documents)
            {
                if (document.TotId.HasValue)
                {
                    availableTotIds.Add(document.TotId.Value);
                }
            }

            var pendingRequests = await _db.ProjectDocumentRequests
                .AsNoTracking()
                .Where(r => r.ProjectId == project.Id && r.Status == ProjectDocumentRequestStatus.Submitted)
                .Include(r => r.Stage)
                .Include(r => r.Document)
                .Include(r => r.RequestedByUser)
                .OrderByDescending(r => r.RequestedAtUtc)
                .ToListAsync(ct);

            foreach (var request in pendingRequests)
            {
                if (request.Document?.TotId is int requestTotId)
                {
                    availableTotIds.Add(requestTotId);
                }
            }

            MediaTotId = NormalizeMediaTotFilter(MediaTotId, availableTotIds);

            var selectedTotId = MediaTotId;

            var documentsForDisplay = selectedTotId.HasValue
                ? documents.Where(d => d.TotId == selectedTotId.Value).ToList()
                : documents;

            var requestsForDisplay = selectedTotId.HasValue
                ? pendingRequests.Where(r => r.Document?.TotId == selectedTotId.Value).ToList()
                : pendingRequests;

            DocumentPendingRequestCount = requestsForDisplay.Count;

            var tz = TimeZoneHelper.GetIst();

            if (isApprover)
            {
                DocumentPendingRequests = requestsForDisplay
                    .OrderByDescending(r => r.RequestedAtUtc)
                    .Take(5)
                    .Select(r => BuildPendingRequestSummary(project.Id, r, tz))
                    .ToList();
            }
            else
            {
                DocumentPendingRequests = Array.Empty<ProjectDocumentPendingRequestViewModel>();
            }

            var pendingByDocumentId = requestsForDisplay
                .Where(r => r.DocumentId.HasValue)
                .GroupBy(r => r.DocumentId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var filteredDocuments = documentsForDisplay
                .Where(d => StageMatches(d.Stage?.StageCode, normalizedStage))
                .OrderBy(d => StageOrder(d.Stage?.StageCode))
                .ThenByDescending(d => d.UploadedAtUtc)
                .ThenBy(d => d.Id)
                .ToList();

            var filteredRequests = requestsForDisplay
                .Where(r => StageMatches(r.Stage?.StageCode, normalizedStage))
                .OrderBy(r => StageOrder(r.Stage?.StageCode))
                .ThenByDescending(r => r.RequestedAtUtc)
                .ThenBy(r => r.Id)
                .ToList();

            var usingPending = string.Equals(normalizedStatus, ProjectDocumentListViewModel.PendingStatusValue, StringComparison.OrdinalIgnoreCase) && isApprover;

            var rows = usingPending
                ? filteredRequests.Select(r => BuildPendingRow(r, tz)).ToList()
                : filteredDocuments.Select(d => BuildDocumentRow(d, pendingByDocumentId, tz)).ToList();

            var totalItems = rows.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)ProjectDocumentListViewModel.DefaultPageSize));

            if (page > totalPages)
            {
                page = totalPages;
            }

            var pageRows = rows
                .Skip((page - 1) * ProjectDocumentListViewModel.DefaultPageSize)
                .Take(ProjectDocumentListViewModel.DefaultPageSize)
                .ToList();

            var groups = pageRows
                .GroupBy(r => r.StageCode, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => StageOrder(g.Key))
                .ThenBy(g => g.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ProjectDocumentStageGroupViewModel(
                    string.IsNullOrEmpty(g.Key) ? null : g.Key,
                    g.First().StageDisplayName,
                    g.ToList()))
                .ToList();

            var stageFilters = BuildStageFilters(documentsForDisplay, requestsForDisplay, normalizedStage);
            var statusFilters = BuildStatusFilters(normalizedStatus, isApprover);

            DocumentSummary = new ProjectDocumentSummaryViewModel
            {
                TotalCount = documents.Count,
                PublishedCount = documents.Count(d => d.Status == ProjectDocumentStatus.Published),
                PendingCount = pendingRequests.Count
            };

            DocumentPage = page;

            DocumentList = new ProjectDocumentListViewModel(
                groups,
                stageFilters,
                statusFilters,
                normalizedStage,
                usingPending ? ProjectDocumentListViewModel.PendingStatusValue : ProjectDocumentListViewModel.PublishedStatusValue,
                page,
                ProjectDocumentListViewModel.DefaultPageSize,
                totalItems);
        }

        private IReadOnlyList<ProjectMediaVideoViewModel> BuildVideoViewModels(Project project)
        {
            if (Videos.Count == 0)
            {
                return Array.Empty<ProjectMediaVideoViewModel>();
            }

            var placeholder = Url.Content("~/img/placeholders/project-video-placeholder.svg") ?? string.Empty;

            return Videos
                .Select(video =>
                {
                    var playbackUrl = Url.Page("/Projects/Videos/Stream", new
                    {
                        id = project.Id,
                        videoId = video.Id,
                        v = video.Version
                    }) ?? string.Empty;

                    string? thumbnailUrl = null;
                    if (!string.IsNullOrWhiteSpace(video.PosterStorageKey))
                    {
                        thumbnailUrl = Url.Page("/Projects/Videos/Poster", new
                        {
                            id = project.Id,
                            videoId = video.Id,
                            v = video.Version
                        });
                    }

                    thumbnailUrl ??= placeholder;

                    TimeSpan? duration = video.DurationSeconds.HasValue
                        ? TimeSpan.FromSeconds(video.DurationSeconds.Value)
                        : (TimeSpan?)null;

                    var title = string.IsNullOrWhiteSpace(video.Title)
                        ? Path.GetFileNameWithoutExtension(video.OriginalFileName)
                        : video.Title!;

                    return new ProjectMediaVideoViewModel(
                        video.Id,
                        title,
                        playbackUrl,
                        thumbnailUrl,
                        duration);
                })
                .ToList();
        }

        private ProjectMediaSummaryViewModel BuildMediaSummary()
        {
            var coverPhoto = CoverPhoto;

            var orderedPhotos = Photos
                .OrderBy(p => p.Ordinal)
                .ThenBy(p => p.Id)
                .ToList();

            var additionalPhotos = orderedPhotos
                .Where(p => coverPhoto is null || p.Id != coverPhoto.Id)
                .ToList();

            var previewPhotos = additionalPhotos
                .Take(ProjectMediaSummaryViewModel.DefaultPreviewCount)
                .ToList();

            var remaining = Math.Max(0, additionalPhotos.Count - previewPhotos.Count);

            return new ProjectMediaSummaryViewModel
            {
                PhotoCount = orderedPhotos.Count,
                AdditionalPhotoCount = additionalPhotos.Count,
                PreviewPhotos = previewPhotos,
                RemainingPhotoCount = remaining,
                CoverPhoto = coverPhoto,
                CoverPhotoVersion = CoverPhotoVersion,
                CoverPhotoUrl = CoverPhotoUrl,
                DocumentCount = DocumentSummary.PublishedCount,
                PendingDocumentCount = DocumentSummary.PendingCount,
                VideoCount = Videos.Count,
                FeaturedVideo = FeaturedVideo,
                FeaturedVideoVersion = FeaturedVideoVersion,
                FeaturedVideoUrl = FeaturedVideoUrl
            };
        }

        private ProjectLifecycleActionsViewModel BuildLifecycleActions(Project project, bool isAdmin, bool isHoD, bool isAssignedHoD)
        {
            var canManage = isAdmin || isHoD || isAssignedHoD;

            var canMarkCompleted = canManage &&
                (project.LifecycleStatus == ProjectLifecycleStatus.Active ||
                 (project.LifecycleStatus == ProjectLifecycleStatus.Completed && project.CompletedOn is null));

            var canEndorse = canManage &&
                project.LifecycleStatus == ProjectLifecycleStatus.Completed &&
                project.CompletedYear.HasValue &&
                project.CompletedOn is null;

            var canCancel = canManage && project.LifecycleStatus == ProjectLifecycleStatus.Active;

            return new ProjectLifecycleActionsViewModel
            {
                Status = project.LifecycleStatus,
                CanManageLifecycle = canManage,
                CanMarkCompleted = canMarkCompleted,
                CanEndorseCompletedDate = canEndorse,
                CanCancel = canCancel,
                CompletedYear = project.CompletedYear,
                CompletedOn = project.CompletedOn,
                CancelledOn = project.CancelledOn,
                CancelReason = project.CancelReason
            };
        }

        private bool CanManageLifecycle(string? userId, string? projectHodId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return false;
            }

            if (User.IsInRole("Admin") || User.IsInRole("HoD"))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(projectHodId) && string.Equals(projectHodId, userId, StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        private ProjectLifecycleSummaryViewModel BuildLifecycleSummary(Project project)
        {
            var facts = new List<ProjectLifecycleSummaryViewModel.LifecycleFact>();
            string? primaryDetail = null;
            string? secondaryDetail = null;

            if (project.LifecycleStatus == ProjectLifecycleStatus.Completed)
            {
                if (project.CompletedOn.HasValue)
                {
                    var completedOn = project.CompletedOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
                    primaryDetail = string.Format(CultureInfo.InvariantCulture, "Project completed on {0}.", completedOn);
                    facts.Add(new ProjectLifecycleSummaryViewModel.LifecycleFact("Completed on", completedOn));
                }
                else
                {
                    primaryDetail = "Project marked as completed.";
                }

                if (project.CompletedYear.HasValue)
                {
                    var yearDisplay = project.CompletedYear.Value.ToString(CultureInfo.InvariantCulture);
                    facts.Add(new ProjectLifecycleSummaryViewModel.LifecycleFact("Completed in", yearDisplay));
                }
            }
            else if (project.LifecycleStatus == ProjectLifecycleStatus.Cancelled)
            {
                if (project.CancelledOn.HasValue)
                {
                    var cancelledOn = project.CancelledOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
                    primaryDetail = string.Format(CultureInfo.InvariantCulture, "Project cancelled on {0}.", cancelledOn);
                    facts.Add(new ProjectLifecycleSummaryViewModel.LifecycleFact("Cancelled on", cancelledOn));
                }
                else
                {
                    primaryDetail = "Project marked as cancelled.";
                }

                if (!string.IsNullOrWhiteSpace(project.CancelReason))
                {
                    secondaryDetail = string.Format(CultureInfo.InvariantCulture, "Reason: {0}", project.CancelReason);
                }
            }
            else if (project.IsLegacy)
            {
                primaryDetail = "Legacy project view — timeline actions disabled.";
            }

            var statusLabel = project.LifecycleStatus switch
            {
                ProjectLifecycleStatus.Completed => "Completed",
                ProjectLifecycleStatus.Cancelled => "Cancelled",
                _ => "Active"
            };

            if (string.IsNullOrWhiteSpace(primaryDetail))
            {
                primaryDetail = project.IsLegacy
                    ? "Legacy project view — timeline actions disabled."
                    : "Project is active.";
            }

            return new ProjectLifecycleSummaryViewModel
            {
                ShowPostCompletionView = project.LifecycleStatus != ProjectLifecycleStatus.Active || project.IsLegacy,
                Status = project.LifecycleStatus,
                StatusLabel = statusLabel,
                IsLegacy = project.IsLegacy,
                PrimaryDetail = primaryDetail,
                SecondaryDetail = secondaryDetail,
                BadgeText = project.IsLegacy ? "Legacy" : null,
                Facts = facts
            };
        }

        private async Task<ProjectRemarkSummaryViewModel> LoadRemarkSummaryAsync(int projectId, CancellationToken ct)
        {
            var remarkCounts = await _db.Remarks
                .AsNoTracking()
                .Where(r => r.ProjectId == projectId && !r.IsDeleted)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Internal = g.Sum(r => r.Type == RemarkType.Internal ? 1 : 0),
                    External = g.Sum(r => r.Type == RemarkType.External ? 1 : 0)
                })
                .SingleOrDefaultAsync(ct);

            var lastRemark = await _db.Remarks
                .AsNoTracking()
                .Where(r => r.ProjectId == projectId && !r.IsDeleted)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Select(r => new
                {
                    r.Id,
                    r.Type,
                    r.Body,
                    r.CreatedAtUtc,
                    r.AuthorRole
                })
                .FirstOrDefaultAsync(ct);

            return new ProjectRemarkSummaryViewModel
            {
                InternalCount = remarkCounts?.Internal ?? 0,
                ExternalCount = remarkCounts?.External ?? 0,
                LastRemarkId = lastRemark?.Id,
                LastRemarkType = lastRemark?.Type,
                LastRemarkActorRole = lastRemark?.AuthorRole,
                LastActivityUtc = lastRemark?.CreatedAtUtc,
                LastRemarkPreview = BuildRemarkPreview(lastRemark?.Body)
            };
        }

        private static string? BuildRemarkPreview(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            var trimmed = body.Trim();
            trimmed = trimmed.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

            const int limit = 140;
            if (trimmed.Length <= limit)
            {
                return trimmed;
            }

            return string.Concat(trimmed.AsSpan(0, limit), "…");
        }

        private ProjectTotSummaryViewModel BuildTotSummary(Project project)
        {
            var tot = project.Tot;
            if (tot is null)
            {
                return new ProjectTotSummaryViewModel
                {
                    HasTotRecord = false,
                    Status = ProjectTotStatus.NotStarted,
                    StatusLabel = "Not tracked",
                    Summary = "Transfer of Technology tracking has not been configured for this project.",
                    PendingRequest = BuildTotRequestSummary(project.TotRequest)
                };
            }

            var facts = new List<ProjectTotSummaryViewModel.TotFact>();

            if (tot.StartedOn.HasValue)
            {
                var started = tot.StartedOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
                facts.Add(new ProjectTotSummaryViewModel.TotFact("Started on", started));
            }

            if (tot.CompletedOn.HasValue)
            {
                var completed = tot.CompletedOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
                facts.Add(new ProjectTotSummaryViewModel.TotFact("Completed on", completed));
            }

            if (tot.LastApprovedOnUtc.HasValue && !string.IsNullOrWhiteSpace(tot.LastApprovedByUserId))
            {
                var approver = tot.LastApprovedByUser?.FullName;
                var approverDisplay = string.IsNullOrWhiteSpace(approver) ? tot.LastApprovedByUserId : approver;
                var approvedOn = TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(tot.LastApprovedOnUtc.Value, DateTimeKind.Utc),
                    TimeZoneHelper.GetIst());
                facts.Add(new ProjectTotSummaryViewModel.TotFact(
                    "Approved",
                    string.Format(CultureInfo.InvariantCulture, "{0} on {1:dd MMM yyyy HH:mm}", approverDisplay, approvedOn)));
            }

            var summary = tot.Status switch
            {
                ProjectTotStatus.NotRequired => "Transfer of Technology was not required for this project.",
                ProjectTotStatus.NotStarted => "Transfer of Technology has not started.",
                ProjectTotStatus.InProgress when tot.StartedOn.HasValue
                    => string.Format(CultureInfo.InvariantCulture, "Transfer of Technology started on {0} and is in progress.", tot.StartedOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)),
                ProjectTotStatus.InProgress => "Transfer of Technology is in progress.",
                ProjectTotStatus.Completed when tot.CompletedOn.HasValue
                    => string.Format(CultureInfo.InvariantCulture, "Transfer of Technology completed on {0}.", tot.CompletedOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)),
                ProjectTotStatus.Completed => "Transfer of Technology is marked as completed.",
                _ => "Transfer of Technology details are unavailable."
            };

            return new ProjectTotSummaryViewModel
            {
                HasTotRecord = true,
                Status = tot.Status,
                StatusLabel = tot.Status switch
                {
                    ProjectTotStatus.NotRequired => "Not required",
                    ProjectTotStatus.NotStarted => "Not started",
                    ProjectTotStatus.InProgress => "In progress",
                    ProjectTotStatus.Completed => "Completed",
                    _ => tot.Status.ToString()
                },
                Summary = summary,
                Remarks = string.IsNullOrWhiteSpace(tot.Remarks) ? null : tot.Remarks,
                Facts = facts,
                LastApprovedBy = string.IsNullOrWhiteSpace(tot.LastApprovedByUser?.FullName)
                    ? tot.LastApprovedByUserId
                    : tot.LastApprovedByUser!.FullName,
                LastApprovedOnUtc = tot.LastApprovedOnUtc,
                PendingRequest = BuildTotRequestSummary(project.TotRequest)
            };
        }

        private static ProjectTotSummaryViewModel.TotRequestSummary? BuildTotRequestSummary(ProjectTotRequest? request)
        {
            if (request is null)
            {
                return null;
            }

            var stateLabel = request.DecisionState switch
            {
                ProjectTotRequestDecisionState.Pending => "Pending approval",
                ProjectTotRequestDecisionState.Approved => "Approved",
                ProjectTotRequestDecisionState.Rejected => "Rejected",
                _ => request.DecisionState.ToString()
            };

            var proposedStatusLabel = request.ProposedStatus switch
            {
                ProjectTotStatus.NotRequired => "Not required",
                ProjectTotStatus.NotStarted => "Not started",
                ProjectTotStatus.InProgress => "In progress",
                ProjectTotStatus.Completed => "Completed",
                _ => request.ProposedStatus.ToString()
            };

            var submittedBy = string.IsNullOrWhiteSpace(request.SubmittedByUser?.FullName)
                ? request.SubmittedByUserId
                : request.SubmittedByUser!.FullName;

            var decidedBy = string.IsNullOrWhiteSpace(request.DecidedByUser?.FullName)
                ? request.DecidedByUserId
                : request.DecidedByUser!.FullName;

            return new ProjectTotSummaryViewModel.TotRequestSummary(
                request.DecisionState,
                stateLabel,
                request.ProposedStatus,
                proposedStatusLabel,
                request.ProposedStartedOn,
                request.ProposedCompletedOn,
                string.IsNullOrWhiteSpace(request.ProposedRemarks) ? null : request.ProposedRemarks,
                submittedBy ?? string.Empty,
                request.SubmittedOnUtc,
                decidedBy,
                request.DecidedOnUtc,
                string.IsNullOrWhiteSpace(request.DecisionRemarks) ? null : request.DecisionRemarks);
        }

        private IReadOnlyList<ProjectDocumentFilterOptionViewModel> BuildStageFilters(
            IReadOnlyCollection<ProjectDocument> documents,
            IReadOnlyCollection<ProjectDocumentRequest> pendingRequests,
            string? selectedStage)
        {
            var filters = new List<ProjectDocumentFilterOptionViewModel>
            {
                new(null, "All stages", string.IsNullOrEmpty(selectedStage))
            };

            var stageCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var code in StageCodes.All)
            {
                stageCodes.Add(code);
            }

            foreach (var stage in Stages)
            {
                if (!string.IsNullOrWhiteSpace(stage.StageCode))
                {
                    stageCodes.Add(stage.StageCode);
                }
            }

            foreach (var document in documents)
            {
                if (!string.IsNullOrWhiteSpace(document.Stage?.StageCode))
                {
                    stageCodes.Add(document.Stage.StageCode);
                }
            }

            foreach (var request in pendingRequests)
            {
                if (!string.IsNullOrWhiteSpace(request.Stage?.StageCode))
                {
                    stageCodes.Add(request.Stage.StageCode);
                }
            }

            var orderedCodes = stageCodes
                .OrderBy(StageOrder)
                .ThenBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var code in orderedCodes)
            {
                var label = string.Format(CultureInfo.InvariantCulture, "{0} ({1})", StageCodes.DisplayNameOf(code), code);
                filters.Add(new ProjectDocumentFilterOptionViewModel(
                    code,
                    label,
                    string.Equals(code, selectedStage, StringComparison.OrdinalIgnoreCase)));
            }

            var hasUnassigned = documents.Any(d => d.StageId is null) || pendingRequests.Any(r => r.StageId is null);

            if (hasUnassigned || string.Equals(selectedStage, ProjectDocumentListViewModel.UnassignedStageValue, StringComparison.OrdinalIgnoreCase))
            {
                filters.Add(new ProjectDocumentFilterOptionViewModel(
                    ProjectDocumentListViewModel.UnassignedStageValue,
                    "General",
                    string.Equals(selectedStage, ProjectDocumentListViewModel.UnassignedStageValue, StringComparison.OrdinalIgnoreCase)));
            }

            return filters;
        }

        private static IReadOnlyList<ProjectDocumentFilterOptionViewModel> BuildStatusFilters(string selectedStatus, bool isApprover)
        {
            var filters = new List<ProjectDocumentFilterOptionViewModel>
            {
                new(ProjectDocumentListViewModel.PublishedStatusValue, "Published", !string.Equals(selectedStatus, ProjectDocumentListViewModel.PendingStatusValue, StringComparison.OrdinalIgnoreCase))
            };

            if (isApprover)
            {
                filters.Add(new ProjectDocumentFilterOptionViewModel(
                    ProjectDocumentListViewModel.PendingStatusValue,
                    "Pending",
                    string.Equals(selectedStatus, ProjectDocumentListViewModel.PendingStatusValue, StringComparison.OrdinalIgnoreCase)));
            }

            return filters;
        }

        private ProjectDocumentRowViewModel BuildDocumentRow(
            ProjectDocument document,
            IReadOnlyDictionary<int, ProjectDocumentRequest> pendingRequests,
            TimeZoneInfo tz)
        {
            var stageCode = document.Stage?.StageCode;
            var stageDisplay = BuildStageDisplayName(stageCode);
            var uploadedBy = FormatUser(document.UploadedByUser);
            var uploadedOn = TimeZoneInfo.ConvertTime(document.UploadedAtUtc, tz);
            var metadata = string.Format(CultureInfo.InvariantCulture, "Uploaded on {0:dd MMM yyyy} by {1}", uploadedOn, uploadedBy);
            var title = string.IsNullOrWhiteSpace(document.Title) ? document.OriginalFileName : document.Title;
            var statusLabel = document.Status == ProjectDocumentStatus.Published ? "Published" : "Removed";
            var statusVariant = document.Status == ProjectDocumentStatus.Published ? "success" : "secondary";
            string? secondarySummary = null;
            ProjectDocumentRequestType? pendingType = null;
            var isPending = false;
            int? requestId = null;

            if (pendingRequests.TryGetValue(document.Id, out var pending))
            {
                isPending = true;
                statusLabel = "Pending";
                statusVariant = "warning";
                pendingType = pending.RequestType;
                requestId = pending.Id;
                var pendingBy = FormatUser(pending.RequestedByUser);
                var pendingOn = TimeZoneInfo.ConvertTime(pending.RequestedAtUtc, tz);
                secondarySummary = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} request submitted on {1:dd MMM yyyy} by {2}",
                    DescribeRequestType(pending.RequestType),
                    pendingOn,
                    pendingBy);
            }

            var previewUrl = Url.Page("/Projects/Documents/Preview", new { documentId = document.Id });
            var totId = document.TotId;

            return new ProjectDocumentRowViewModel(
                stageCode,
                stageDisplay,
                document.Id,
                requestId,
                title,
                document.OriginalFileName,
                FormatFileSize(document.FileSize),
                metadata,
                statusLabel,
                statusVariant,
                isPending,
                document.Status == ProjectDocumentStatus.SoftDeleted,
                previewUrl,
                secondarySummary,
                pendingType,
                totId,
                totId.HasValue);
        }

        private ProjectDocumentRowViewModel BuildPendingRow(ProjectDocumentRequest request, TimeZoneInfo tz)
        {
            var stageCode = request.Stage?.StageCode;
            var stageDisplay = BuildStageDisplayName(stageCode);
            var requestedBy = FormatUser(request.RequestedByUser);
            var requestedOn = TimeZoneInfo.ConvertTime(request.RequestedAtUtc, tz);
            var metadata = string.Format(CultureInfo.InvariantCulture, "Requested on {0:dd MMM yyyy} by {1}", requestedOn, requestedBy);
            var title = string.IsNullOrWhiteSpace(request.Title)
                ? (request.OriginalFileName ?? request.Document?.OriginalFileName ?? "Pending document")
                : request.Title;
            var previewUrl = request.DocumentId.HasValue
                ? Url.Page("/Projects/Documents/Preview", new { documentId = request.DocumentId.Value })
                : null;

            var secondarySummary = string.Format(
                CultureInfo.InvariantCulture,
                "{0} request awaiting review",
                DescribeRequestType(request.RequestType));

            var fileName = request.OriginalFileName ?? request.Document?.OriginalFileName;
            var totId = request.TotId ?? request.Document?.TotId;

            return new ProjectDocumentRowViewModel(
                stageCode,
                stageDisplay,
                request.DocumentId,
                request.Id,
                title,
                fileName,
                FormatFileSize(request.FileSize ?? request.Document?.FileSize),
                metadata,
                "Pending",
                "warning",
                true,
                false,
                previewUrl,
                secondarySummary,
                request.RequestType,
                totId,
                totId.HasValue);
        }

        private ProjectDocumentPendingRequestViewModel BuildPendingRequestSummary(int projectId, ProjectDocumentRequest request, TimeZoneInfo tz)
        {
            var requestedBy = FormatUser(request.RequestedByUser);
            var requestedOn = TimeZoneInfo.ConvertTime(request.RequestedAtUtc, tz);
            var summary = string.Format(CultureInfo.InvariantCulture, "Requested on {0:dd MMM yyyy, HH:mm} by {1}", requestedOn, requestedBy);
            var fileName = request.OriginalFileName ?? request.Document?.OriginalFileName ?? "—";
            var reviewUrl = Url.Page("/Projects/Documents/Approvals/Review", new { id = projectId, requestId = request.Id }) ?? string.Empty;
            var rowVersion = request.RowVersion is { Length: > 0 }
                ? Convert.ToBase64String(request.RowVersion)
                : string.Empty;

            return new ProjectDocumentPendingRequestViewModel(
                request.Id,
                string.IsNullOrWhiteSpace(request.Title) ? fileName : request.Title,
                BuildStageDisplayName(request.Stage?.StageCode),
                string.Format(CultureInfo.InvariantCulture, "{0} request", DescribeRequestType(request.RequestType)),
                summary,
                fileName,
                FormatFileSize(request.FileSize ?? request.Document?.FileSize),
                rowVersion,
                reviewUrl,
                request.RequestedAtUtc,
                requestedBy);
        }

        private static string BuildStageDisplayName(string? stageCode)
        {
            if (string.IsNullOrWhiteSpace(stageCode))
            {
                return "General";
            }

            return string.Format(CultureInfo.InvariantCulture, "{0} ({1})", StageCodes.DisplayNameOf(stageCode), stageCode);
        }

        private static string? NormalizeDocumentStage(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(value, ProjectDocumentListViewModel.UnassignedStageValue, StringComparison.OrdinalIgnoreCase))
            {
                return ProjectDocumentListViewModel.UnassignedStageValue;
            }

            return value.Trim().ToUpperInvariant();
        }

        private static string NormalizeDocumentStatus(string? value, bool isApprover)
        {
            if (isApprover && string.Equals(value, ProjectDocumentListViewModel.PendingStatusValue, StringComparison.OrdinalIgnoreCase))
            {
                return ProjectDocumentListViewModel.PendingStatusValue;
            }

            return ProjectDocumentListViewModel.PublishedStatusValue;
        }

        private static int? NormalizeMediaTotFilter(int? value, IReadOnlyCollection<int> availableTotIds)
        {
            if (!value.HasValue || availableTotIds.Count == 0)
            {
                return null;
            }

            return availableTotIds.Contains(value.Value) ? value : null;
        }

        private static bool StageMatches(string? stageCode, string? filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }

            if (string.Equals(filter, ProjectDocumentListViewModel.UnassignedStageValue, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrEmpty(stageCode);
            }

            return string.Equals(stageCode, filter, StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatFileSize(long? bytes)
        {
            if (!bytes.HasValue)
            {
                return "—";
            }

            if (bytes.Value < 1024)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0} B", bytes.Value);
            }

            double value = bytes.Value;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            var unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            if (unit == 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0} {1}", bytes.Value, units[unit]);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", value, units[unit]);
        }

        private static string FormatUser(ApplicationUser? user)
        {
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

            return string.IsNullOrWhiteSpace(user.Email) ? "Unknown" : user.Email!;
        }

        private static string DescribeRequestType(ProjectDocumentRequestType type) => type switch
        {
            ProjectDocumentRequestType.Upload => "Upload",
            ProjectDocumentRequestType.Replace => "Replacement",
            ProjectDocumentRequestType.Delete => "Removal",
            _ => "Request"
        };

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
            var proposedTechnicalCategoryId = payload.TechnicalCategoryId;
            var proposedUnitId = payload.SponsoringUnitId;
            var proposedLineDirectorateId = payload.SponsoringLineDirectorateId;

            var currentUnitDisplay = Format(project.SponsoringUnit?.Name);
            var currentLineDirectorateDisplay = Format(project.SponsoringLineDirectorate?.Name);

            var originalTechnicalCategoryDisplay = "—";
            if (request.OriginalTechnicalCategoryId.HasValue)
            {
                var originalTechnicalPath = await BuildTechnicalCategoryPathAsync(request.OriginalTechnicalCategoryId.Value, ct);
                if (originalTechnicalPath.Any())
                {
                    originalTechnicalCategoryDisplay = string.Join(" › ", originalTechnicalPath.Select(c => c.Name));
                }
            }

            var currentTechnicalCategoryDisplay = TechnicalCategoryPath.Any()
                ? string.Join(" › ", TechnicalCategoryPath.Select(c => c.Name))
                : "—";

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

            string proposedTechnicalCategoryDisplay = "—";
            if (proposedTechnicalCategoryId.HasValue)
            {
                var proposedTechnicalPath = await BuildTechnicalCategoryPathAsync(proposedTechnicalCategoryId.Value, ct);
                if (proposedTechnicalPath.Any())
                {
                    proposedTechnicalCategoryDisplay = string.Join(" › ", proposedTechnicalPath.Select(c => c.Name));
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
                    case ProjectMetaChangeDriftFields.TechnicalCategory:
                        drift.Add(new ProjectMetaChangeDriftVm("Technical category", originalTechnicalCategoryDisplay, currentTechnicalCategoryDisplay, false));
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

            var nameField = new ProjectMetaChangeFieldVm(
                project.Name,
                proposedNameDisplay,
                !string.Equals(project.Name, proposedNameRaw, StringComparison.Ordinal));
            var descriptionField = new ProjectMetaChangeFieldVm(
                Format(project.Description),
                proposedDescriptionDisplay,
                !string.Equals(project.Description ?? string.Empty, proposedDescription ?? string.Empty, StringComparison.Ordinal));
            var caseFileField = new ProjectMetaChangeFieldVm(
                Format(project.CaseFileNumber),
                proposedCaseFileDisplay,
                !string.Equals(project.CaseFileNumber ?? string.Empty, proposedCaseFileNumber ?? string.Empty, StringComparison.Ordinal));
            var categoryField = new ProjectMetaChangeFieldVm(
                currentCategoryDisplay,
                proposedCategoryDisplay,
                project.CategoryId != proposedCategoryId);
            var technicalCategoryField = new ProjectMetaChangeFieldVm(
                currentTechnicalCategoryDisplay,
                proposedTechnicalCategoryDisplay,
                project.TechnicalCategoryId != proposedTechnicalCategoryId);
            var unitField = new ProjectMetaChangeFieldVm(
                currentUnitDisplay,
                proposedUnitDisplay,
                project.SponsoringUnitId != proposedUnitId);
            var lineDirectorateField = new ProjectMetaChangeFieldVm(
                currentLineDirectorateDisplay,
                proposedLineDisplay,
                project.SponsoringLineDirectorateId != proposedLineDirectorateId);

            var summaryFields = new List<string>();

            void AddSummary(ProjectMetaChangeFieldVm field, string label)
            {
                if (field.HasChanged)
                {
                    summaryFields.Add(label);
                }
            }

            AddSummary(nameField, "name");
            AddSummary(descriptionField, "description");
            AddSummary(caseFileField, "case file number");
            AddSummary(categoryField, "category");
            AddSummary(technicalCategoryField, "technical category");
            AddSummary(unitField, "sponsoring unit");
            AddSummary(lineDirectorateField, "sponsoring line directorate");

            string summary;
            if (summaryFields.Count == 0)
            {
                summary = "Requested metadata review.";
            }
            else if (summaryFields.Count == 1)
            {
                summary = string.Format(CultureInfo.InvariantCulture, "Requested update to {0}.", summaryFields[0]);
            }
            else if (summaryFields.Count == 2)
            {
                summary = string.Format(CultureInfo.InvariantCulture, "Requested updates to {0} and {1}.", summaryFields[0], summaryFields[1]);
            }
            else
            {
                var leading = string.Join(", ", summaryFields.Take(summaryFields.Count - 1));
                summary = string.Format(CultureInfo.InvariantCulture, "Requested updates to {0}, and {1}.", leading, summaryFields[^1]);
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
                OriginalTechnicalCategory = originalTechnicalCategoryDisplay,
                Name = nameField,
                Description = descriptionField,
                CaseFileNumber = caseFileField,
                Category = categoryField,
                TechnicalCategory = technicalCategoryField,
                SponsoringUnit = unitField,
                SponsoringLineDirectorate = lineDirectorateField,
                HasDrift = drift.Count > 0,
                Drift = drift,
                Summary = summary
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

        private async Task<IReadOnlyList<TechnicalCategory>> BuildTechnicalCategoryPathAsync(int technicalCategoryId, CancellationToken ct)
        {
            var path = new List<TechnicalCategory>();
            var visited = new HashSet<int>();
            var currentId = technicalCategoryId;

            while (true)
            {
                if (!visited.Add(currentId))
                {
                    break;
                }

                var category = await _db.TechnicalCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == currentId, ct);
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
