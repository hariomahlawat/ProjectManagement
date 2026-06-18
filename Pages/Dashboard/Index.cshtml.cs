using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Dashboard.Components.FfcSimulatorMap;
using ProjectManagement.Areas.Dashboard.Components.OpsSignals;
using ProjectManagement.Areas.Dashboard.Components.ProjectPulse;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Notebook;
using ProjectManagement.Services.Dashboard;
using ProjectManagement.Helpers;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Models.Stages;
using ProjectManagement.Models.Execution;
using ProjectManagement.ViewModels.Dashboard;
using ProjectManagement.ViewModels.Notebook;
using Microsoft.Extensions.Logging;

namespace ProjectManagement.Pages.Dashboard
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly INotebookService _notebook;
        private readonly UserManager<ApplicationUser> _users;
        private readonly Data.ApplicationDbContext _db;
        private readonly IProjectPulseService _projectPulse;
        private readonly IOpsSignalsService _opsSignalsService;
        private readonly ISearchHealthService _searchHealthService;
        private readonly ILogger<IndexModel> _logger;
        private static readonly TimeZoneInfo IST = IstClock.TimeZone;

        public IndexModel(
            INotebookService notebook,
            UserManager<ApplicationUser> users,
            Data.ApplicationDbContext db,
            IProjectPulseService projectPulse,
            IOpsSignalsService opsSignalsService,
            ISearchHealthService searchHealthService,
            ILogger<IndexModel> logger)
        {
            _notebook = notebook;
            _users = users;
            _db = db;
            _projectPulse = projectPulse;
            _opsSignalsService = opsSignalsService;
            _searchHealthService = searchHealthService;
            _logger = logger;
        }

        public NotebookWidgetVm? NotebookWidget { get; set; }
        public List<UpcomingEventVM> UpcomingEvents { get; set; } = new();
        public List<MyProjectsSection> MyProjectSections { get; private set; } = new();
        public bool HasMyProjects => MyProjectSections.Any(section => section.Items.Count > 0);
        // SECTION: Dashboard KPI widgets
        public ProjectPulseVm? ProjectPulse { get; private set; }
        public OpsSignalsVm OpsSignals { get; private set; } = new() { Tiles = Array.Empty<OpsTileVm>() };
        public FfcSimulatorMapVm FfcSimulatorMap { get; private set; } = new();
        public SearchHealthVm SearchHealth { get; private set; } = new();
        // END SECTION

        // SECTION: My Projects widget state
        public bool ShowMyProjectsWidget { get; private set; }
        public bool ShowEmptyMyProjectsMessage { get; private set; }
        // END SECTION

        public class UpcomingEventVM
        {
            public Guid? Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string When { get; set; } = string.Empty;
            public bool IsHoliday { get; set; }
        }


        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            var uid = _users.GetUserId(User);
            // SECTION: Notebook widget load with fault isolation
            if (uid != null)
            {
                try
                {
                    NotebookWidget = await _notebook.GetWidgetAsync(uid, take: 5, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Dashboard widget failed: Notebook");
                    NotebookWidget = null;
                }
            }
            // END SECTION

            var nowUtc = DateTime.UtcNow;
            var rangeEnd = nowUtc.AddDays(30);

            var upcoming = new List<(Guid? Id, string Title, DateTime StartUtc, DateTime EndUtc, bool IsAllDay, bool IsHoliday)>();

            // SECTION: Upcoming events and holidays load
            try
            {
                var events = await _db.Events.AsNoTracking()
                    .Where(e => !e.IsDeleted && e.StartUtc >= nowUtc && e.StartUtc < rangeEnd)
                    .OrderBy(e => e.StartUtc)
                    .Take(15)
                    .Select(e => new { e.Id, e.Title, e.StartUtc, e.EndUtc, e.IsAllDay })
                    .ToListAsync();
                foreach (var ev in events)
                {
                    upcoming.Add((ev.Id, ev.Title, ev.StartUtc.UtcDateTime, ev.EndUtc.UtcDateTime, ev.IsAllDay, false));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard widget failed: Upcoming events (Events)");
            }

            var todayIst = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, IST));
            var windowEndIst = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(rangeEnd, IST));
            try
            {
                var celebrations = await _db.Celebrations.AsNoTracking()
                    .Where(c => c.DeletedUtc == null)
                    .ToListAsync();

                foreach (var celebration in celebrations)
                {
                    try
                    {
                        var nextOccurrence = CelebrationHelpers.NextOccurrenceLocal(celebration, todayIst);

                        var startLocal = CelebrationHelpers.ToLocalDateTime(nextOccurrence);
                        var startUtc = startLocal.UtcDateTime;
                        if (startUtc >= nowUtc && startUtc < rangeEnd)
                        {
                            var titlePrefix = celebration.EventType switch
                            {
                                CelebrationType.Birthday => "Birthday",
                                CelebrationType.Anniversary => "Anniversary",
                                _ => celebration.EventType.ToString()
                            };
                            var title = $"{titlePrefix}: {CelebrationHelpers.DisplayName(celebration)}";
                            var endUtc = CelebrationHelpers.ToLocalDateTime(nextOccurrence.AddDays(1)).UtcDateTime;
                            upcoming.Add((celebration.Id, title, startUtc, endUtc, true, false));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Dashboard widget failed: Celebration {CelebrationId}", celebration.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard widget failed: Celebrations block");
            }

            try
            {
                var holidays = await _db.Holidays.AsNoTracking()
                    .Where(h => h.Date >= todayIst && h.Date <= windowEndIst)
                    .ToListAsync();

                foreach (var holiday in holidays)
                {
                    var startLocal = DateTime.SpecifyKind(holiday.Date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
                    var endLocal = DateTime.SpecifyKind(holiday.Date.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
                    var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, IST);
                    var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, IST);
                    upcoming.Add((null, $"Holiday: {holiday.Name}", startUtc, endUtc, true, true));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard widget failed: Holidays block");
            }
            // END SECTION

            foreach (var item in upcoming
                .OrderBy(x => x.StartUtc)
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .Take(10))
            {
                var startLocal = TimeZoneInfo.ConvertTimeFromUtc(item.StartUtc, IST);
                var endLocal = TimeZoneInfo.ConvertTimeFromUtc(item.EndUtc, IST);
                string when;
                if (item.IsAllDay)
                {
                    var startDate = DateOnly.FromDateTime(startLocal);
                    var inclusiveEndCandidate = endLocal.AddDays(-1);
                    if (inclusiveEndCandidate < startLocal)
                    {
                        inclusiveEndCandidate = startLocal;
                    }
                    var endDate = DateOnly.FromDateTime(inclusiveEndCandidate);
                    when = startDate == endDate
                        ? startDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
                        : string.Format(
                            CultureInfo.InvariantCulture,
                            "{0:dd MMM yyyy} – {1:dd MMM yyyy}",
                            startDate,
                            endDate);
                }
                else
                {
                    var startStr = startLocal.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
                    if (startLocal == endLocal)
                    {
                        when = startStr;
                    }
                    else if (startLocal.Date == endLocal.Date)
                    {
                        when = string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} – {1:HH:mm}",
                            startStr,
                            endLocal);
                    }
                    else
                    {
                        when = string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} – {1:dd MMM yyyy, HH:mm}",
                            startStr,
                            endLocal);
                    }
                }
                UpcomingEvents.Add(new UpcomingEventVM { Id = item.Id, Title = item.Title, When = when, IsHoliday = item.IsHoliday });
            }

            var isProjectOfficer = User.IsInRole(RoleNames.ProjectOfficer);
            var isHod = User.IsInRole(RoleNames.HoD);
            var isComdt = User.IsInRole(RoleNames.Comdt);
            var isMco = User.IsInRole(RoleNames.Mco);

            ShowMyProjectsWidget = isProjectOfficer || isHod || isComdt || isMco;
            ShowEmptyMyProjectsMessage = isProjectOfficer || isHod;

            if (uid != null && ShowMyProjectsWidget)
            {
                try
                {
                    await LoadMyProjectsAsync(uid, isProjectOfficer, isHod, isComdt || isMco, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Dashboard widget failed: My Projects");
                    MyProjectSections = new List<MyProjectsSection>();
                }
            }

            // SECTION: KPI widgets load with fault isolation
            try
            {
                ProjectPulse = await _projectPulse.GetAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard widget failed: ProjectPulse");
                ProjectPulse = null;
            }

            try
            {
                OpsSignals = await _opsSignalsService.GetAsync(
                    from: null,
                    to: null,
                    userId: uid ?? string.Empty,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard widget failed: OpsSignals");
                OpsSignals = new OpsSignalsVm { Tiles = Array.Empty<OpsTileVm>() };
            }

            try
            {
                SearchHealth = await _searchHealthService.GetAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard widget failed: SearchHealth");
                SearchHealth = new SearchHealthVm();
            }

            try
            {
                var ffcRows = await FfcCountryRollupDataSource.LoadAsync(_db, cancellationToken);
                // SECTION: FFC simulator map footprint and totals
                var ffcFootprintCountries = ffcRows
                    .Where(row => row.Total > 0)
                    .Select(row => new FfcSimulatorCountryVm
                    {
                        CountryId = row.CountryId,
                        Iso3 = row.Iso3,
                        Name = row.Name,
                        Installed = row.Installed,
                        Delivered = row.Delivered,
                        Planned = row.Planned,
                        TotalUnits = row.Total
                    })
                    .ToList();

                var totalInstalled = ffcRows.Sum(row => row.Installed);
                var totalDelivered = ffcRows.Sum(row => row.Delivered);
                var totalPlanned = ffcRows.Sum(row => row.Planned);

                FfcSimulatorMap = new FfcSimulatorMapVm
                {
                    Countries = ffcFootprintCountries,
                    TotalInstalled = totalInstalled,
                    TotalDelivered = totalDelivered,
                    TotalPlanned = totalPlanned
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard widget failed: FFC rollup");
                FfcSimulatorMap = new FfcSimulatorMapVm();
            }
            // END SECTION
        }

        private async Task LoadMyProjectsAsync(
            string userId,
            bool includeOfficerSection,
            bool includeHodSection,
            bool includeAllOngoingSection,
            CancellationToken cancellationToken)
        {
            // SECTION: Aggregate accessible projects by category for My Projects widget
            var projectSummaries = new List<ProjectAssignmentSummary>();

            if (includeAllOngoingSection)
            {
                projectSummaries = await OnlyOngoing(_db.Projects.AsNoTracking())
                    .OrderBy(p => p.Name)
                    .Select(ProjectSummarySelector)
                    .ToListAsync(cancellationToken);
            }
            else
            {
                if (includeOfficerSection)
                {
                    var officerProjects = await OnlyOngoing(_db.Projects.AsNoTracking())
                        .Where(p => p.LeadPoUserId == userId)
                        .OrderBy(p => p.Name)
                        .Select(ProjectSummarySelector)
                        .ToListAsync(cancellationToken);

                    projectSummaries.AddRange(officerProjects);
                }

                if (includeHodSection)
                {
                    var hodProjects = await OnlyOngoing(_db.Projects.AsNoTracking())
                        .Where(p => p.HodUserId == userId)
                        .OrderBy(p => p.Name)
                        .Select(ProjectSummarySelector)
                        .ToListAsync(cancellationToken);

                    projectSummaries.AddRange(hodProjects);
                }
            }

            var distinctSummaries = projectSummaries
                .GroupBy(project => project.Id)
                .Select(group => group.First())
                .ToList();

            var groupedSections = distinctSummaries
                .GroupBy(summary => NormalizeCategory(summary.Category))
                .OrderBy(group => CategoryOrderKey(group.Key))
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new MyProjectsSection
                {
                    Title = group.Key,
                    Items = group
                        .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(CreateProjectItem)
                        .ToList()
                })
                .ToList();

            MyProjectSections = groupedSections;
            await AttachStageSummariesAsync(MyProjectSections, cancellationToken);
            // END SECTION

            // SECTION: Local helpers
            string NormalizeCategory(string? category) => string.IsNullOrWhiteSpace(category)
                ? "Other R&D Projects"
                : category;

            int CategoryOrderKey(string category) => category switch
            {
                "CoE" => 0,
                "DCD Projects" => 1,
                "Other R&D Projects" => 2,
                _ => 5
            };

            MyProjectItem CreateProjectItem(ProjectAssignmentSummary summary)
            {
                string? coverImageUrl = summary.CoverPhotoId.HasValue
                    ? Url.Page("/Projects/Photos/View", new
                    {
                        id = summary.Id,
                        photoId = summary.CoverPhotoId.Value,
                        size = "xs",
                        v = summary.CoverPhotoVersion
                    })
                    : null;

                return new MyProjectItem
                {
                    ProjectId = summary.Id,
                    Name = summary.Name,
                    Category = string.IsNullOrWhiteSpace(summary.Category) ? null : summary.Category,
                    CoverImageUrl = coverImageUrl
                };
            }
            // END SECTION

        }

        private async Task AttachStageSummariesAsync(List<MyProjectsSection> sections, CancellationToken cancellationToken)
        {
            var projectIds = sections
                .SelectMany(section => section.Items)
                .Select(item => item.ProjectId)
                .Distinct()
                .ToArray();

            if (projectIds.Length == 0)
            {
                return;
            }

            var stageRows = await _db.ProjectStages
                .AsNoTracking()
                .Where(stage => projectIds.Contains(stage.ProjectId))
                .ToListAsync(cancellationToken);

            var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

            foreach (var item in sections.SelectMany(section => section.Items))
            {
                var summary = BuildStageSummary(item.ProjectId, stageRows, today);
                if (summary is not null)
                {
                    item.StageSummary = summary;
                }
            }
        }

        private static MyProjectStageSummary? BuildStageSummary(int projectId, List<ProjectStage> allStages, DateOnly today)
        {
            if (StageCodes.All.Length == 0)
            {
                return null;
            }

            var stagesForProject = allStages
                .Where(stage => stage.ProjectId == projectId)
                .ToDictionary(stage => stage.StageCode, stage => stage, StringComparer.OrdinalIgnoreCase);

            // SECTION: Determine current stage
            var orderedStages = StageCodes.All
                .Select(code =>
                {
                    stagesForProject.TryGetValue(code, out var stageRow);
                    return new
                    {
                        Code = code,
                        Row = stageRow,
                        Status = stageRow?.Status ?? StageStatus.NotStarted,
                        stageRow?.PlannedDue
                    };
                })
                .ToList();

            var currentStage = orderedStages.FirstOrDefault(stage => stage.Status == StageStatus.InProgress)
                               ?? orderedStages.FirstOrDefault(stage => stage.Status == StageStatus.NotStarted && stage.PlannedDue is not null);

            if (currentStage is null)
            {
                return null;
            }
            // END SECTION

            // SECTION: PDC calculations
            var plannedDue = currentStage.PlannedDue;
            var currentStatus = currentStage.Status;

            int? daysToPdc = null;
            var isOverdue = false;

            if (plannedDue.HasValue)
            {
                var diffDays = (plannedDue.Value.ToDateTime(TimeOnly.MinValue) - today.ToDateTime(TimeOnly.MinValue)).Days;
                if (diffDays < 0)
                {
                    isOverdue = currentStatus != StageStatus.Completed;
                    daysToPdc = Math.Abs(diffDays);
                }
                else
                {
                    daysToPdc = diffDays;
                }
            }
            // END SECTION

            // SECTION: Build summary
            return new MyProjectStageSummary
            {
                StageCode = currentStage.Code,
                StageName = StageCodes.DisplayNameOf(currentStage.Code),
                PlannedDue = plannedDue,
                Status = currentStatus,
                IsOverdue = isOverdue,
                DaysToPdc = daysToPdc
            };
            // END SECTION
        }

        // SECTION: My Projects helpers
        private static readonly Expression<Func<Project, ProjectAssignmentSummary>> ProjectSummarySelector = project => new ProjectAssignmentSummary
        {
            Id = project.Id,
            Name = project.Name,
            Category = project.Category != null ? project.Category.Name : null,
            CoverPhotoId = project.CoverPhotoId,
            CoverPhotoVersion = project.CoverPhotoVersion
        };

        private static IQueryable<Project> OnlyOngoing(IQueryable<Project> query)
        {
            return query.Where(p => !p.IsDeleted
                && !p.IsArchived
                && p.LifecycleStatus != ProjectLifecycleStatus.Completed
                && p.LifecycleStatus != ProjectLifecycleStatus.Cancelled);
        }
        // END SECTION

        public sealed class MyProjectsSection
        {
            public string Title { get; init; } = string.Empty;
            public List<MyProjectItem> Items { get; init; } = new();
        }

        public sealed class MyProjectItem
        {
            public int ProjectId { get; init; }
            public string Name { get; init; } = string.Empty;
            public string? Category { get; init; }
            public string? CoverImageUrl { get; init; }
            public MyProjectStageSummary? StageSummary { get; set; }
        }

        public sealed class MyProjectStageSummary
        {
            public string StageCode { get; init; } = string.Empty;
            public string StageName { get; init; } = string.Empty;
            public DateOnly? PlannedDue { get; init; }
            public StageStatus Status { get; init; }
            public bool IsOverdue { get; init; }
            public int? DaysToPdc { get; init; }
        }

        private sealed class ProjectAssignmentSummary
        {
            public int Id { get; init; }
            public string Name { get; init; } = string.Empty;
            public string? Category { get; init; }
            public int? CoverPhotoId { get; init; }
            public int CoverPhotoVersion { get; init; }
        }

    }
}
