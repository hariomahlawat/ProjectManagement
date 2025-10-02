using System;
using System.Collections.Generic;
using System.Globalization;
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
        public ProjectRemarksPanelViewModel RemarksPanel { get; private set; } = ProjectRemarksPanelViewModel.Empty;
        public bool HasBackfill { get; private set; }
        public bool RequiresPlanApproval { get; private set; }
        public string? CurrentUserId { get; private set; }
        public ProjectMetaChangeRequestVm? MetaChangeRequest { get; private set; }
        public IReadOnlyList<ProjectPhoto> Photos { get; private set; } = Array.Empty<ProjectPhoto>();
        public ProjectPhoto? CoverPhoto { get; private set; }
        public int? CoverPhotoVersion { get; private set; }
        public string? CoverPhotoUrl { get; private set; }

        [BindProperty(SupportsGet = true)]
        public string? DocumentStageFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? DocumentStatusFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public int DocumentPage { get; set; } = 1;

        public ProjectDocumentListViewModel DocumentList { get; private set; } = ProjectDocumentListViewModel.Empty;

        public IReadOnlyList<ProjectDocumentPendingRequestViewModel> DocumentPendingRequests { get; private set; } = Array.Empty<ProjectDocumentPendingRequestViewModel>();

        public bool IsDocumentApprover { get; private set; }

        public int DocumentPendingRequestCount { get; private set; }

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

            var isAdmin = User.IsInRole("Admin");
            var isHoD = User.IsInRole("HoD");
            var isProjectOfficer = User.IsInRole("Project Officer");
            var isThisProjectsPo = isProjectOfficer && string.Equals(project.LeadPoUserId, CurrentUserId, StringComparison.Ordinal);

            await LoadDocumentOverviewAsync(project, isAdmin, isHoD, ct);

            RemarksPanel = await BuildRemarksPanelAsync(project, isThisProjectsPo, ct);

            return Page();
        }

        private async Task<ProjectRemarksPanelViewModel> BuildRemarksPanelAsync(Project project, bool isThisProjectsPo, CancellationToken ct)
        {
            var stageOptions = Stages
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

            var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var user = await _users.GetUserAsync(User);
            if (user is null)
            {
                return new ProjectRemarksPanelViewModel
                {
                    ProjectId = project.Id,
                    StageOptions = stageOptions,
                    RoleOptions = roleOptions,
                    Today = today
                };
            }

            var userRoles = await _users.GetRolesAsync(user);
            var remarkRoles = userRoles
                .Select(role => RemarkActorRoleExtensions.TryParse(role, out var parsed) ? parsed : RemarkActorRole.Unknown)
                .Where(role => role != RemarkActorRole.Unknown)
                .Distinct()
                .ToList();

            var actorRole = SelectDefaultRemarkRole(remarkRoles);
            var actorRoleCanonical = actorRole == RemarkActorRole.Unknown ? null : actorRole.ToString();
            var actorRoleLabel = actorRole == RemarkActorRole.Unknown ? null : BuildRoleDisplayName(actorRole);

            var canOverride = remarkRoles.Any(role => role is RemarkActorRole.HeadOfDepartment or RemarkActorRole.Commandant or RemarkActorRole.Administrator);
            var canPostAsHoDOrAbove = remarkRoles.Any(role => role is RemarkActorRole.HeadOfDepartment or RemarkActorRole.Commandant or RemarkActorRole.Administrator);
            var canPostAsMco = remarkRoles.Contains(RemarkActorRole.Mco);
            var canPostAsPo = remarkRoles.Contains(RemarkActorRole.ProjectOfficer) && isThisProjectsPo;

            var showComposer = canPostAsHoDOrAbove || canPostAsMco || canPostAsPo;
            var allowExternal = canPostAsHoDOrAbove;

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
                ShowDeletedToggle = remarkRoles.Contains(RemarkActorRole.Administrator),
                ActorHasOverride = canOverride,
                StageOptions = stageOptions,
                RoleOptions = roleOptions,
                Today = today
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

        private async Task LoadDocumentOverviewAsync(Project project, bool isAdmin, bool isHoD, CancellationToken ct)
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

            var pendingRequests = await _db.ProjectDocumentRequests
                .AsNoTracking()
                .Where(r => r.ProjectId == project.Id && r.Status == ProjectDocumentRequestStatus.Submitted)
                .Include(r => r.Stage)
                .Include(r => r.Document)
                .Include(r => r.RequestedByUser)
                .OrderByDescending(r => r.RequestedAtUtc)
                .ToListAsync(ct);

            DocumentPendingRequestCount = pendingRequests.Count;

            var tz = TimeZoneHelper.GetIst();

            if (isApprover)
            {
                DocumentPendingRequests = pendingRequests
                    .OrderByDescending(r => r.RequestedAtUtc)
                    .Take(5)
                    .Select(r => BuildPendingRequestSummary(project.Id, r, tz))
                    .ToList();
            }
            else
            {
                DocumentPendingRequests = Array.Empty<ProjectDocumentPendingRequestViewModel>();
            }

            var pendingByDocumentId = pendingRequests
                .Where(r => r.DocumentId.HasValue)
                .GroupBy(r => r.DocumentId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var filteredDocuments = documents
                .Where(d => StageMatches(d.Stage?.StageCode, normalizedStage))
                .OrderBy(d => StageOrder(d.Stage?.StageCode))
                .ThenByDescending(d => d.UploadedAtUtc)
                .ThenBy(d => d.Id)
                .ToList();

            var filteredRequests = pendingRequests
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

            var stageFilters = BuildStageFilters(documents, pendingRequests, normalizedStage);
            var statusFilters = BuildStatusFilters(normalizedStatus, isApprover);

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
                pendingType);
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
                request.RequestType);
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
