using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Areas.ProjectOfficeReports.Api;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Models;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Areas.ProjectOfficeReports.Api
{
    [ApiController]
    [Route("api/proliferation")]
    [Authorize]
    [AutoValidateAntiforgeryToken]
    public sealed class ProliferationController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ProliferationTrackerReadService _readSvc;
        private readonly ProliferationSubmissionService _submitSvc;
        private readonly ProliferationManageService _manageSvc;
        private readonly ProliferationOverviewService _overviewSvc;
        private readonly ProliferationAggregateReadService _aggregateSvc;
        private readonly IProliferationExportService _exportService;
        private readonly ProliferationDataQualityService _dataQualityService;
        private readonly ILogger<ProliferationController> _logger;

        public ProliferationController(
            ApplicationDbContext db,
            ProliferationTrackerReadService readSvc,
            ProliferationSubmissionService submitSvc,
            ProliferationManageService manageSvc,
            ProliferationOverviewService overviewSvc,
            ProliferationAggregateReadService aggregateSvc,
            IProliferationExportService exportService,
            ProliferationDataQualityService dataQualityService,
            ILogger<ProliferationController> logger)
        {
            _db = db;
            _readSvc = readSvc;
            _submitSvc = submitSvc;
            _manageSvc = manageSvc;
            _overviewSvc = overviewSvc;
            _aggregateSvc = aggregateSvc;
            _exportService = exportService;
            _dataQualityService = dataQualityService;
            _logger = logger;
        }

        [HttpGet("list")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.SubmitProliferationTracker)]
        public async Task<ActionResult<ProliferationManageListResponseDto>> GetManageList([FromQuery] ProliferationManageListQueryDto query, CancellationToken ct)
        {
            var kind = ParseKind(query.Kind);
            var approvalStatus = ParseApprovalStatus(query.ApprovalStatus);

            var request = new ProliferationManageListRequest(
                query.ProjectId,
                query.Source,
                query.Year,
                kind,
                approvalStatus,
                query.Search,
                query.Page,
                query.PageSize);
            var result = await _manageSvc.GetListAsync(request, ct);

            var items = result.Items
                .Select(item => new ProliferationManageListItemDto
                {
                    Id = item.Id,
                    Kind = item.Kind == ProliferationRecordKind.Yearly ? "yearly" : "granular",
                    ProjectId = item.ProjectId,
                    ProjectName = item.ProjectName,
                    ProjectCode = item.ProjectCode,
                    Source = item.Source,
                    SourceLabel = item.SourceLabel,
                    UnitName = item.UnitName,
                    Year = item.Year,
                    Month = item.ProliferationDate?.Month,
                    ProliferationDateUtc = item.ProliferationDate?.ToDateTime(TimeOnly.MinValue),
                    Quantity = item.Quantity,
                    ApprovalStatus = item.ApprovalStatus.ToString(),
                    CreatedOnUtc = item.CreatedOnUtc,
                    LastUpdatedOnUtc = item.LastUpdatedOnUtc,
                    ApprovedOnUtc = item.ApprovedOnUtc,
                    AmendsId = item.AmendsId
                })
                .ToList();

            var payload = new ProliferationManageListResponseDto
            {
                Total = result.Total,
                Page = result.Page,
                PageSize = result.PageSize,
                Items = items
            };

            return Ok(payload);
        }

        [HttpGet("preferences/overrides")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ManageProliferationPreferences)]
        public async Task<ActionResult<IReadOnlyList<ProliferationPreferenceOverrideDto>>> GetPreferenceOverrides(
            [FromQuery] ProliferationPreferenceOverrideQueryDto query,
            CancellationToken ct)
        {
            var request = new ProliferationPreferenceOverrideRequest(query.ProjectId, query.Source, query.Year, query.Search);
            var overrides = await _overviewSvc.GetPreferenceOverridesAsync(request, ct);

            var payload = overrides
                .Select(item => new ProliferationPreferenceOverrideDto
                {
                    Id = item.Id,
                    ProjectId = item.ProjectId,
                    ProjectName = item.ProjectName,
                    ProjectCode = item.ProjectCode,
                    Source = item.Source,
                    SourceValue = (int)item.Source,
                    SourceLabel = item.Source.ToDisplayName(),
                    Year = item.Year,
                    Mode = item.Mode,
                    ModeLabel = ProliferationAggregateReadService.GetCalculationLabel(item.Mode, item.Source),
                    EffectiveMode = item.EffectiveMode,
                    EffectiveModeLabel = ProliferationAggregateReadService.GetCalculationLabel(item.EffectiveMode, item.Source),
                    SetByUserId = item.SetByUserId,
                    SetByDisplayName = item.SetByDisplayName,
                    SetOnUtc = item.SetOnUtc,
                    HasYearly = item.HasYearly,
                    HasGranular = item.HasGranular,
                    HasApprovedYearly = item.HasApprovedYearly,
                    HasApprovedGranular = item.HasApprovedGranular,
                    EffectiveTotal = item.EffectiveTotal
                })
                .ToList();

            return Ok(payload);
        }

        [HttpGet("preferences/overrides/export")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ManageProliferationPreferences)]
        public async Task<IActionResult> ExportPreferenceOverrides(
            [FromQuery] ProliferationPreferenceOverrideQueryDto query,
            CancellationToken ct)
        {
            var request = new ProliferationPreferenceOverrideRequest(query.ProjectId, query.Source, query.Year, query.Search);
            var overrides = await _overviewSvc.GetPreferenceOverridesAsync(request, ct);

            var builder = new StringBuilder();
            builder.AppendLine(string.Join(',', new[]
            {
                "Project",
                "Project Code",
                "Source",
                "Year",
                "Configured counting rule",
                "Effective counting rule",
                "Has approved annual quantity",
                "Has approved detailed entries",
                "Set By",
                "Set By User ID",
                "Updated On (UTC)"
            }));

            foreach (var item in overrides.OrderByDescending(o => o.SetOnUtc))
            {
                var updated = item.SetOnUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
                builder.AppendLine(string.Join(',', new[]
                {
                    CsvEscape(item.ProjectName),
                    CsvEscape(item.ProjectCode),
                    CsvEscape(item.Source.ToDisplayName()),
                    CsvEscape(item.Year.ToString(CultureInfo.InvariantCulture)),
                    CsvEscape(item.Mode.ToString()),
                    CsvEscape(item.EffectiveMode.ToString()),
                    CsvEscape(item.HasApprovedYearly ? "Yes" : "No"),
                    CsvEscape(item.HasApprovedGranular ? "Yes" : "No"),
                    CsvEscape(item.SetByDisplayName),
                    CsvEscape(item.SetByUserId),
                    CsvEscape(updated)
                }));
            }

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var fileName = $"proliferation-counting-exceptions-{timestamp}.csv";
            var buffer = Encoding.UTF8.GetBytes(builder.ToString());
            return File(buffer, "text/csv", fileName);
        }

        [HttpGet("yearly/{id:guid}")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.SubmitProliferationTracker)]
        public async Task<ActionResult<ProliferationYearlyDetailDto>> GetYearly(Guid id, CancellationToken ct)
        {
            var detail = await _manageSvc.GetYearlyAsync(id, ct);
            if (detail is null)
            {
                return NotFound();
            }

            return new ProliferationYearlyDetailDto
            {
                Id = detail.Id,
                ProjectId = detail.ProjectId,
                Source = detail.Source,
                Year = detail.Year,
                TotalQuantity = detail.TotalQuantity,
                Remarks = detail.Remarks,
                RowVersion = EncodeRowVersion(detail.RowVersion),
                ApprovalStatus = detail.ApprovalStatus.ToString(),
                SubmittedByUserId = detail.SubmittedByUserId,
                ApprovedByUserId = detail.ApprovedByUserId,
                CreatedOnUtc = detail.CreatedOnUtc,
                LastUpdatedOnUtc = detail.LastUpdatedOnUtc,
                ApprovedOnUtc = detail.ApprovedOnUtc,
                AmendsId = detail.AmendsId,
                PendingAmendmentId = detail.PendingAmendmentId
            };
        }

        [HttpGet("granular/{id:guid}")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.SubmitProliferationTracker)]
        public async Task<ActionResult<ProliferationGranularDetailDto>> GetGranular(Guid id, CancellationToken ct)
        {
            var detail = await _manageSvc.GetGranularAsync(id, ct);
            if (detail is null)
            {
                return NotFound();
            }

            return new ProliferationGranularDetailDto
            {
                Id = detail.Id,
                ProjectId = detail.ProjectId,
                Source = detail.Source,
                ProliferationDateUtc = detail.ProliferationDate.ToDateTime(TimeOnly.MinValue),
                UnitName = detail.UnitName,
                Quantity = detail.Quantity,
                Remarks = detail.Remarks,
                RowVersion = EncodeRowVersion(detail.RowVersion),
                ApprovalStatus = detail.ApprovalStatus.ToString(),
                SubmittedByUserId = detail.SubmittedByUserId,
                ApprovedByUserId = detail.ApprovedByUserId,
                CreatedOnUtc = detail.CreatedOnUtc,
                LastUpdatedOnUtc = detail.LastUpdatedOnUtc,
                ApprovedOnUtc = detail.ApprovedOnUtc,
                AmendsId = detail.AmendsId,
                PendingAmendmentId = detail.PendingAmendmentId
            };
        }

        [HttpGet("projects")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
        public async Task<ActionResult<ProliferationProjectLookupResponseDto>> GetEligibleProjects(
            [FromQuery] string? q,
            [FromQuery] int? projectCategoryId,
            [FromQuery] int? technicalCategoryId,
            [FromQuery] int take = 200,
            CancellationToken ct = default,
            [FromQuery] bool includeLegacy = false)
        {
            var projects = includeLegacy
                ? ProliferationProjectEligibility.CompletedVisibleProjects(_db.Projects.AsNoTracking())
                : ProliferationProjectEligibility.EligibleForNewRecords(_db.Projects.AsNoTracking());

            if (projectCategoryId.HasValue)
            {
                projects = projects.Where(p => p.CategoryId == projectCategoryId.Value);
            }

            if (technicalCategoryId.HasValue)
            {
                projects = projects.Where(p => p.TechnicalCategoryId == technicalCategoryId.Value);
            }

            var candidates = await projects
                .Select(p => new ProjectLookupCandidate(
                    p.Id,
                    p.Name,
                    p.CaseFileNumber,
                    p.Category != null ? p.Category.Name : null,
                    p.TechnicalCategory != null ? p.TechnicalCategory.Name : null,
                    p.IsBuild))
                .ToListAsync(ct);

            var term = NormalizeProjectSearchText(q);
            var limit = Math.Clamp(take, 1, 500);

            IEnumerable<ProjectLookupCandidate> ranked = candidates;
            if (!string.IsNullOrWhiteSpace(term))
            {
                ranked = candidates
                    .Select(project => new { Project = project, Rank = GetProjectSearchRank(project, term) })
                    .Where(item => item.Rank < int.MaxValue)
                    .OrderBy(item => item.Rank)
                    .ThenBy(item => item.Project.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Project.Code, StringComparer.OrdinalIgnoreCase)
                    .Select(item => item.Project);
            }
            else
            {
                ranked = candidates
                    .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(project => project.Code, StringComparer.OrdinalIgnoreCase);
            }

            var matched = ranked.ToList();
            var items = matched
                .Take(limit)
                .Select(ToProjectLookupDto)
                .ToList();

            return Ok(new ProliferationProjectLookupResponseDto
            {
                Total = matched.Count,
                Returned = items.Count,
                EligibilityDescription = includeLegacy
                    ? "Completed projects, including legacy repeat-build projects, available for historical review."
                    : "Completed non-repeat-build projects eligible for new proliferation entry.",
                Items = items
            });
        }

        [HttpGet("projects/{id:int}")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
        public async Task<ActionResult<ProliferationProjectLookupDto>> GetEligibleProjectById(
            int id,
            [FromQuery] int? projectCategoryId,
            [FromQuery] int? technicalCategoryId,
            CancellationToken ct,
            [FromQuery] bool includeLegacy = false)
        {
            if (id <= 0)
            {
                return BadRequest("A valid project id is required.");
            }

            var projects = includeLegacy
                ? ProliferationProjectEligibility.CompletedVisibleProjects(_db.Projects.AsNoTracking())
                : ProliferationProjectEligibility.EligibleForNewRecords(_db.Projects.AsNoTracking());

            projects = projects.Where(p => p.Id == id);

            if (projectCategoryId.HasValue)
            {
                projects = projects.Where(p => p.CategoryId == projectCategoryId.Value);
            }

            if (technicalCategoryId.HasValue)
            {
                projects = projects.Where(p => p.TechnicalCategoryId == technicalCategoryId.Value);
            }

            var project = await projects
                .Select(p => new ProjectLookupCandidate(
                    p.Id,
                    p.Name,
                    p.CaseFileNumber,
                    p.Category != null ? p.Category.Name : null,
                    p.TechnicalCategory != null ? p.TechnicalCategory.Name : null,
                    p.IsBuild))
                .FirstOrDefaultAsync(ct);

            return project is null ? NotFound() : Ok(ToProjectLookupDto(project));
        }

        [HttpGet("lookups")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
        public async Task<ActionResult<ProliferationLookupsDto>> GetLookups(CancellationToken ct)
        {
            var projectCategories = await _db.ProjectCategories
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .Select(c => new ProliferationLookupOptionDto
                {
                    Id = c.Id,
                    Name = c.Name
                })
                .ToListAsync(ct);

            var technicalCategories = await _db.TechnicalCategories
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .Select(c => new ProliferationLookupOptionDto
                {
                    Id = c.Id,
                    Name = c.Name
                })
                .ToListAsync(ct);

            return new ProliferationLookupsDto
            {
                ProjectCategories = projectCategories,
                TechnicalCategories = technicalCategories
            };
        }

        [HttpGet("groups")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
        public async Task<ActionResult<ProliferationGroupedResponseDto>> GetGroupedRecords(
            [FromQuery] ProliferationGroupedQueryDto query,
            CancellationToken ct)
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize <= 0 ? 25 : query.PageSize, 10, 100);

            var aggregates = await _aggregateSvc.GetApprovedAggregatesAsync(query.ProjectId, ct);
            IEnumerable<ProliferationAggregateRow> filtered = aggregates;

            if (query.Source.HasValue)
            {
                filtered = filtered.Where(x => x.Source == query.Source.Value);
            }

            if (query.Year.HasValue)
            {
                filtered = filtered.Where(x => x.Year == query.Year.Value);
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim();
                var like = $"%{EscapeLikePattern(term)}%";

                var matchingDetailed = await _db.ProliferationGranularEntries
                    .AsNoTracking()
                    .Where(x => x.ApprovalStatus == ApprovalStatus.Approved)
                    .Where(x =>
                        EF.Functions.ILike(x.UnitName, like, "\\") ||
                        (x.Remarks != null && EF.Functions.ILike(x.Remarks, like, "\\")))
                    .Select(x => new { x.ProjectId, x.Source, Year = x.ProliferationDate.Year })
                    .Distinct()
                    .ToListAsync(ct);

                var matchingAnnual = await _db.ProliferationYearlies
                    .AsNoTracking()
                    .Where(x => x.ApprovalStatus == ApprovalStatus.Approved && x.Remarks != null)
                    .Where(x => EF.Functions.ILike(x.Remarks!, like, "\\"))
                    .Select(x => new { x.ProjectId, x.Source, x.Year })
                    .Distinct()
                    .ToListAsync(ct);

                var matchingKeys = matchingDetailed
                    .Select(x => (x.ProjectId, x.Source, x.Year))
                    .Concat(matchingAnnual.Select(x => (x.ProjectId, x.Source, x.Year)))
                    .ToHashSet();

                filtered = filtered.Where(x =>
                    x.ProjectName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(x.ProjectCode) &&
                     x.ProjectCode.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    matchingKeys.Contains((x.ProjectId, x.Source, x.Year)));
            }

            var filteredRows = filtered.ToList();
            var sort = NormalizeGroupedSort(query.Sort);
            var todayIst = DateOnly.FromDateTime(IstClock.ToIst(DateTime.UtcNow));

            IReadOnlyDictionary<(int ProjectId, ProliferationSource Source, int Year), DateOnly> latestDetailedDates =
                new Dictionary<(int ProjectId, ProliferationSource Source, int Year), DateOnly>();

            if (sort == "latest-proliferation" && filteredRows.Count > 0)
            {
                var filteredProjectIds = filteredRows
                    .Select(x => x.ProjectId)
                    .Distinct()
                    .ToArray();

                var filteredYears = filteredRows
                    .Select(x => x.Year)
                    .Distinct()
                    .ToArray();
                var filteredSources = filteredRows
                    .Select(x => x.Source)
                    .Distinct()
                    .ToArray();

                var detailedDates = await _db.ProliferationGranularEntries
                    .AsNoTracking()
                    .Where(x =>
                        x.ApprovalStatus == ApprovalStatus.Approved &&
                        x.ProliferationDate <= todayIst &&
                        filteredProjectIds.Contains(x.ProjectId) &&
                        filteredSources.Contains(x.Source) &&
                        filteredYears.Contains(x.ProliferationDate.Year))
                    .Select(x => new
                    {
                        x.ProjectId,
                        x.Source,
                        Year = x.ProliferationDate.Year,
                        x.ProliferationDate
                    })
                    .ToListAsync(ct);

                latestDetailedDates = detailedDates
                    .GroupBy(x => (x.ProjectId, x.Source, x.Year))
                    .ToDictionary(
                        group => group.Key,
                        group => group.Max(x => x.ProliferationDate));
            }

            long BusinessSortKey(ProliferationAggregateRow row)
            {
                if (latestDetailedDates.TryGetValue((row.ProjectId, row.Source, row.Year), out var date))
                {
                    return (date.Year * 10_000L) + (date.Month * 100L) + date.Day;
                }

                var maximumChronologicalYear = DateTime.UtcNow.Year + 1;
                if (row.Year is < 2000 || row.Year > maximumChronologicalYear)
                {
                    return long.MinValue;
                }

                return (row.Year * 10_000L) + 101L;
            }

            var ordered = sort switch
            {
                "latest-activity" => filteredRows
                    .OrderByDescending(x => x.LastUpdatedOnUtc ?? DateTime.MinValue)
                    .ThenByDescending(x => x.Year)
                    .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Source)
                    .ToList(),
                "project" => filteredRows
                    .OrderBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(x => x.Year)
                    .ThenBy(x => x.Source)
                    .ToList(),
                "year" => filteredRows
                    .OrderByDescending(x => x.Year)
                    .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Source)
                    .ToList(),
                _ => filteredRows
                    .OrderByDescending(BusinessSortKey)
                    .ThenByDescending(x => x.Year)
                    .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Source)
                    .ToList()
            };

            var total = ordered.Count;
            var items = ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(row => new ProliferationGroupedRowDto
                {
                    ProjectId = row.ProjectId,
                    ProjectName = row.ProjectName,
                    ProjectCode = row.ProjectCode,
                    Source = row.Source,
                    SourceLabel = row.SourceLabel,
                    Year = row.Year,
                    AnnualQuantity = row.AnnualQuantity,
                    DetailedQuantity = row.DetailedQuantity,
                    DetailedEntryCount = row.DetailedEntryCount,
                    ReportedTotal = row.ReportedTotal,
                    CalculationLabel = row.CalculationLabel,
                    EffectiveMode = row.EffectiveMode.ToString(),
                    CountedBasis = row.CountedBasis.ToString(),
                    HasCountingException = row.HasCountingException,
                    LastUpdatedOnUtc = row.LastUpdatedOnUtc
                })
                .ToList();

            return Ok(new ProliferationGroupedResponseDto
            {
                Total = total,
                Page = page,
                PageSize = pageSize,
                Items = items
            });
        }

        [HttpGet("groups/{projectId:int}/entries")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
        public async Task<ActionResult<IReadOnlyList<ProliferationGroupedDetailedEntryDto>>> GetGroupedDetailedEntries(
            int projectId,
            [FromQuery] ProliferationSource source,
            [FromQuery] int year,
            CancellationToken ct)
        {
            if (projectId <= 0)
            {
                return BadRequest("A valid project id is required.");
            }

            if (source is not (ProliferationSource.Sdd or ProliferationSource.Abw515))
            {
                return BadRequest("A valid source is required.");
            }

            if (year is < 2000 or > 3000)
            {
                return BadRequest("Year must be between 2000 and 3000.");
            }

            var rows = await _db.ProliferationGranularEntries
                .AsNoTracking()
                .Where(x =>
                    x.ProjectId == projectId &&
                    x.Source == source &&
                    x.ProliferationDate.Year == year &&
                    x.ApprovalStatus == ApprovalStatus.Approved)
                .OrderByDescending(x => x.ProliferationDate)
                .ThenBy(x => x.UnitName)
                .Select(x => new ProliferationGroupedDetailedEntryProjection(
                    x.Id,
                    x.ProjectId,
                    x.Source,
                    x.ProliferationDate.Year,
                    x.ProliferationDate,
                    x.UnitName,
                    x.Quantity,
                    x.Remarks))
                .ToListAsync(ct);

            var entries = rows
                .Select(x => new ProliferationGroupedDetailedEntryDto
                {
                    Id = x.Id,
                    ProliferationDate = x.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    UnitName = x.UnitName,
                    Quantity = x.Quantity,
                    Remarks = x.Remarks
                })
                .ToList();

            return Ok(entries);
        }

        [HttpGet("data-quality")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ApproveProliferationTracker)]
        public async Task<ActionResult<ProliferationDataQualityResponseDto>> GetDataQualityIssues(
            [FromQuery] ProliferationDataQualityQueryDto query,
            CancellationToken ct)
        {
            var result = await _dataQualityService.GetIssuesAsync(
                new ProliferationDataQualityQuery(query.ProjectId, query.IssueType, query.Search, query.Page, query.PageSize),
                ct);

            return Ok(new ProliferationDataQualityResponseDto
            {
                Total = result.Total,
                Page = result.Page,
                PageSize = result.PageSize,
                InvalidDateOrYearCount = result.InvalidDateOrYearCount,
                MissingUnitCount = result.MissingUnitCount,
                InvalidQuantityCount = result.InvalidQuantityCount,
                PossibleDuplicateCount = result.PossibleDuplicateCount,
                RepeatBuildLinkCount = result.RepeatBuildLinkCount,
                Items = result.Items.Select(item => new ProliferationDataQualityIssueDto
                {
                    IssueKey = item.IssueKey,
                    IssueType = item.IssueType,
                    Severity = item.Severity,
                    RecordKind = item.RecordKind == ProliferationRecordKind.Yearly ? "yearly" : "granular",
                    RecordId = item.RecordId,
                    ProjectId = item.ProjectId,
                    ProjectName = item.ProjectName,
                    ProjectCode = item.ProjectCode,
                    Source = item.Source,
                    SourceLabel = item.SourceLabel,
                    Year = item.Year,
                    ProliferationDate = item.ProliferationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    UnitName = item.UnitName,
                    Quantity = item.Quantity,
                    ApprovalStatus = item.ApprovalStatus.ToString(),
                    LastUpdatedOnUtc = item.LastUpdatedOnUtc,
                    RowVersion = item.RowVersion,
                    Description = item.Description,
                    CanCorrect = item.CanCorrect,
                    RelatedRecordCount = item.RelatedRecordCount
                }).ToList()
            });
        }

        [HttpPost("data-quality/{kind}/{id:guid}/correct")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ApproveProliferationTracker)]
        public async Task<IActionResult> CorrectDataQualityIssue(
            string kind,
            Guid id,
            [FromBody] ProliferationDataQualityCorrectionDto dto,
            CancellationToken ct)
        {
            var recordKind = ParseKind(kind);
            if (!recordKind.HasValue)
            {
                return BadRequest("Record kind must be yearly or granular.");
            }

            if (!string.Equals(dto.RecordKind, kind, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Record kind does not match the selected issue.");
            }

            DateOnly? correctedDate = dto.CorrectedDateUtc.HasValue
                ? DateOnly.FromDateTime(dto.CorrectedDateUtc.Value)
                : null;

            var result = await _dataQualityService.CorrectAsync(
                new ProliferationDataQualityCorrection(
                    recordKind.Value,
                    id,
                    dto.RowVersion,
                    dto.CorrectedYear,
                    correctedDate,
                    dto.CorrectedUnitName,
                    dto.CorrectedQuantity,
                    dto.Reason),
                User,
                ct);

            return ToActionResult(result);
        }

        [HttpPost("yearly")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.SubmitProliferationTracker)]
        public async Task<IActionResult> CreateYearly([FromBody] ProliferationYearlyCreateDto dto, CancellationToken ct)
        {
            var proj = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.ProjectId, ct);
            if (proj == null) return NotFound("Project not found");
            if (proj.LifecycleStatus != ProjectLifecycleStatus.Completed)
                return BadRequest("Only completed projects are eligible");

            var result = await _submitSvc.CreateYearlyAsync(dto, User, ct);
            return result.Success ? Ok(new { id = result.EntityId }) : BadRequest(result.Error);
        }

        [HttpPost("granular")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.SubmitProliferationTracker)]
        public async Task<IActionResult> CreateGranular([FromBody] ProliferationGranularCreateDto dto, CancellationToken ct)
        {
            var proj = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.ProjectId, ct);
            if (proj == null) return NotFound("Project not found");
            if (proj.LifecycleStatus != ProjectLifecycleStatus.Completed)
                return BadRequest("Only completed projects are eligible");

            try
            {
                var result = await _submitSvc.CreateGranularAsync(dto, User, ct);
                return result.Success ? Ok(new { id = result.EntityId }) : BadRequest(result.Error);
            }
            catch (DbUpdateException ex)
            {
                var root = ex.GetBaseException();
                _logger.LogError(ex, "Granular save failed. {Message}", root.Message);

                return Problem(
                    title: "Unable to save the detailed entry",
                    detail: "The entry could not be saved. Review the values and try again.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        [HttpPut("yearly/{id:guid}")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.SubmitProliferationTracker)]
        public async Task<IActionResult> UpdateYearly(Guid id, [FromBody] ProliferationYearlyUpdateDto dto, CancellationToken ct)
        {
            var result = await _submitSvc.UpdateYearlyAsync(id, dto, User, ct);
            return ToActionResult(result);
        }

        [HttpPut("granular/{id:guid}")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.SubmitProliferationTracker)]
        public async Task<IActionResult> UpdateGranular(Guid id, [FromBody] ProliferationGranularUpdateDto dto, CancellationToken ct)
        {
            var result = await _submitSvc.UpdateGranularAsync(id, dto, User, ct);
            return ToActionResult(result);
        }

        [HttpPost("yearly/{id:guid}/decision")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ApproveProliferationTracker)]
        public async Task<IActionResult> DecideYearly(Guid id, [FromBody] ProliferationApprovalDecisionDto dto, CancellationToken ct)
        {
            if (dto is null)
            {
                return BadRequest("Decision payload is required.");
            }

            var result = await _submitSvc.DecideYearlyAsync(id, dto.Approve, dto.RowVersion, dto.Reason, User, ct);
            return ToActionResult(result);
        }

        [HttpPost("granular/{id:guid}/decision")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ApproveProliferationTracker)]
        public async Task<IActionResult> DecideGranular(Guid id, [FromBody] ProliferationApprovalDecisionDto dto, CancellationToken ct)
        {
            if (dto is null)
            {
                return BadRequest("Decision payload is required.");
            }

            var result = await _submitSvc.DecideGranularAsync(id, dto.Approve, dto.RowVersion, dto.Reason, User, ct);
            return ToActionResult(result);
        }

        [HttpDelete("yearly/{id:guid}")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.SubmitProliferationTracker)]
        public async Task<IActionResult> DeleteYearly(Guid id, [FromQuery] string? rowVersion, CancellationToken ct)
        {
            var result = await _submitSvc.DeleteYearlyAsync(id, rowVersion, User, ct);
            return ToActionResult(result, noContent: true);
        }

        [HttpDelete("granular/{id:guid}")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.SubmitProliferationTracker)]
        public async Task<IActionResult> DeleteGranular(Guid id, [FromQuery] string? rowVersion, CancellationToken ct)
        {
            var result = await _submitSvc.DeleteGranularAsync(id, rowVersion, User, ct);
            return ToActionResult(result, noContent: true);
        }

        // SECTION: Counting preview
        // The Records editor and the counting-rule editor ask the server what a save or a
        // different rule would do to a total, so the counting rule is never repeated in JS.
        [HttpGet("counting-preview")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.SubmitProliferationTracker)]
        public async Task<ActionResult<ProliferationCountingPreviewDto>> GetCountingPreview(
            [FromQuery] ProliferationCountingPreviewQueryDto query,
            CancellationToken ct)
        {
            if (query.ProjectId <= 0
                || query.Source is not (ProliferationSource.Sdd or ProliferationSource.Abw515)
                || !ProliferationYearPolicy.IsValid(query.Year, DateTimeOffset.UtcNow))
            {
                return BadRequest(new { message = "Select a project, source and valid year." });
            }

            YearPreferenceMode? previewMode = null;
            if (string.Equals(query.Mode, "default", StringComparison.OrdinalIgnoreCase))
            {
                previewMode = ProliferationAggregateReadService.GetDefaultMode(query.Source);
            }
            else if (!string.IsNullOrWhiteSpace(query.Mode))
            {
                if (!Enum.TryParse<YearPreferenceMode>(query.Mode, ignoreCase: true, out var parsed)
                    || !Enum.IsDefined(parsed))
                {
                    return BadRequest(new { message = "Select a valid counting rule." });
                }

                previewMode = parsed;
            }

            var preview = await _aggregateSvc.GetCountingPreviewAsync(
                query.ProjectId,
                query.Source,
                query.Year,
                previewMode,
                ParseKind(query.Kind),
                query.Quantity,
                query.RecordId,
                ct);

            return new ProliferationCountingPreviewDto
            {
                AnnualQuantity = preview.AnnualQuantity,
                DetailedQuantity = preview.DetailedQuantity,
                CurrentTotal = preview.CurrentTotal,
                CurrentRuleLabel = ProliferationAggregateReadService.GetCalculationLabel(preview.CurrentMode, query.Source),
                PreviewMode = preview.PreviewMode,
                PreviewRuleLabel = ProliferationAggregateReadService.GetCalculationLabel(preview.PreviewMode, query.Source),
                PreviewTotal = preview.PreviewTotal,
                PreviewCountedBasis = preview.PreviewBasis.ToString(),
                QuantityCounted = preview.QuantityCounted,
                HasBothRecordTypes = preview.HasBothRecordTypes
            };
        }

        [HttpGet("year-preference")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
        public async Task<ActionResult<ProliferationYearPreferenceDto>> GetYearPreference([FromQuery] int projectId, [FromQuery] ProliferationSource source, [FromQuery] int year, CancellationToken ct)
        {
            if (projectId <= 0)
            {
                return BadRequest("ProjectId is required.");
            }

            if (year < 2000)
            {
                return BadRequest("Year is required.");
            }

            var preference = await _db.Set<ProliferationYearPreference>()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.Source == source && p.Year == year, ct);

            if (preference is null)
            {
                return NotFound();
            }

            return new ProliferationYearPreferenceDto
            {
                ProjectId = preference.ProjectId,
                Source = preference.Source,
                Year = preference.Year,
                Mode = preference.Mode
            };
        }

        [HttpPost("year-preference")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ApproveProliferationTracker)]
        public async Task<IActionResult> SetPreference([FromBody] ProliferationYearPreferenceDto dto, CancellationToken ct)
        {
            var ok = await _submitSvc.SetYearPreferenceAsync(dto, User, ct);
            return ok.Success ? Ok() : BadRequest(ok.Error);
        }

        [HttpGet("export")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
        public async Task<IActionResult> Export([FromQuery] ProliferationOverviewQuery q, CancellationToken ct)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            DateOnly? from = q.FromDateUtc.HasValue ? DateOnly.FromDateTime(q.FromDateUtc.Value) : null;
            DateOnly? to = q.ToDateUtc.HasValue ? DateOnly.FromDateTime(q.ToDateUtc.Value) : null;
            IReadOnlyCollection<int>? years = q.Years is { Length: > 0 } ? q.Years.ToList() : null;

            var request = new ProliferationExportRequest(
                Years: years,
                FromDate: from,
                ToDate: to,
                Source: q.Source,
                ProjectId: q.ProjectId,
                ProjectCategoryId: q.ProjectCategoryId,
                TechnicalCategoryId: q.TechnicalCategoryId,
                Search: q.Search,
                RequestedByUserId: userId);

            var result = await _exportService.ExportAsync(request, ct);
            if (!result.Success || result.File is null)
            {
                var message = result.Errors.FirstOrDefault() ?? "Export failed.";
                return BadRequest(message);
            }

            return File(result.File.Content, result.File.ContentType, result.File.FileName);
        }

        private IActionResult ToActionResult(ServiceResult result, bool noContent = false)
        {
            if (result.Success)
            {
                return noContent ? NoContent() : Ok(new { id = result.EntityId });
            }

            if (string.Equals(result.Error, "Record not found.", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound();
            }

            // JSON so the Records and data-quality pages can show the service's own message;
            // they deliberately ignore plain-text bodies.
            return BadRequest(new { message = result.Error });
        }

        private static string CsvEscape(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            // Prevent spreadsheet applications from interpreting exported text as a formula.
            var safeValue = value[0] is '=' or '+' or '-' or '@' or '\t'
                ? $"'{value}"
                : value;

            var needsEscaping = safeValue.Contains(',') || safeValue.Contains('"') || safeValue.Contains('\n') || safeValue.Contains('\r');
            if (!needsEscaping)
            {
                return safeValue;
            }

            var escaped = safeValue.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

        // SECTION: Search helpers
        private static ProliferationProjectLookupDto ToProjectLookupDto(ProjectLookupCandidate project)
            => new()
            {
                Id = project.Id,
                Name = project.Name,
                Acronym = ExtractProjectAcronym(project.Name, project.Code),
                Code = project.Code,
                ProjectCategory = project.ProjectCategory,
                TechnicalCategory = project.TechnicalCategory,
                Status = "Completed",
                IsRepeatBuild = project.IsBuild,
                IsEligibleForNewEntry = !project.IsBuild
            };

        private static int GetProjectSearchRank(ProjectLookupCandidate project, string normalizedTerm)
        {
            var normalizedName = NormalizeProjectSearchText(project.Name);
            var normalizedCode = NormalizeProjectSearchText(project.Code);
            var normalizedAcronym = NormalizeProjectSearchText(ExtractProjectAcronym(project.Name, project.Code));

            if (!string.IsNullOrEmpty(normalizedAcronym) && normalizedAcronym == normalizedTerm) return 0;
            if (!string.IsNullOrEmpty(normalizedCode) && normalizedCode == normalizedTerm) return 1;
            if (normalizedName.StartsWith(normalizedTerm, StringComparison.Ordinal)) return 2;
            if (!string.IsNullOrEmpty(normalizedAcronym) && normalizedAcronym.StartsWith(normalizedTerm, StringComparison.Ordinal)) return 3;
            if (!string.IsNullOrEmpty(normalizedCode) && normalizedCode.Contains(normalizedTerm, StringComparison.Ordinal)) return 4;
            if (normalizedName.Contains(normalizedTerm, StringComparison.Ordinal)) return 5;
            return int.MaxValue;
        }

        private static string NormalizeProjectSearchText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToUpperInvariant(character));
                }
            }
            return builder.ToString();
        }

        private static string? ExtractProjectAcronym(string? name, string? code)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                var close = name.LastIndexOf(')');
                var open = close > 0 ? name.LastIndexOf('(', close) : -1;
                if (open >= 0 && close > open + 1)
                {
                    var parenthetical = NormalizeProjectSearchText(name[(open + 1)..close]);
                    if (parenthetical.Length is >= 2 and <= 20 && parenthetical.Any(char.IsLetter))
                    {
                        return parenthetical;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(code))
            {
                var segments = code
                    .Split(new[] { '/', '\\', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(segment => NormalizeProjectSearchText(segment))
                    .Where(segment => segment.Length is >= 2 and <= 20 && segment.Any(char.IsLetter))
                    .Where(segment => !segment.Equals("SDD", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(segment => segment.Length)
                    .ToList();
                if (segments.Count > 0) return segments[0];
            }

            if (string.IsNullOrWhiteSpace(name)) return null;
            var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "A", "AN", "AND", "BASED", "FOR", "OF", "ON", "THE", "TO", "WITH"
            };
            var initials = name
                .Split(new[] { ' ', '-', '/', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(word => !ignored.Contains(word))
                .Where(word => word.Any(char.IsLetterOrDigit))
                .Select(word => char.ToUpperInvariant(word.First(char.IsLetterOrDigit)))
                .Take(12)
                .ToArray();
            return initials.Length >= 2 ? new string(initials) : null;
        }

        private static string EscapeLikePattern(string value)
        {
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal);
        }

        // SECTION: Manage list helpers
        private static ApprovalStatus? ParseApprovalStatus(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var text = value.Trim();

            if (text.Equals("pending", StringComparison.OrdinalIgnoreCase))
            {
                return ApprovalStatus.Pending;
            }

            if (text.Equals("approved", StringComparison.OrdinalIgnoreCase))
            {
                return ApprovalStatus.Approved;
            }

            if (text.Equals("rejected", StringComparison.OrdinalIgnoreCase))
            {
                return ApprovalStatus.Rejected;
            }

            return null;
        }

        private static string NormalizeGroupedSort(string? value) =>
            value?.Trim().ToLowerInvariant() switch
            {
                "latest-activity" => "latest-activity",
                "project" => "project",
                "year" => "year",
                _ => "latest-proliferation"
            };

        private static ProliferationRecordKind? ParseKind(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (string.Equals(value, "yearly", StringComparison.OrdinalIgnoreCase))
            {
                return ProliferationRecordKind.Yearly;
            }

            if (string.Equals(value, "granular", StringComparison.OrdinalIgnoreCase))
            {
                return ProliferationRecordKind.Granular;
            }

            return null;
        }

        private static string EncodeRowVersion(byte[]? rowVersion)
            => rowVersion is { Length: > 0 } ? Convert.ToBase64String(rowVersion) : string.Empty;

        private sealed record ProjectLookupCandidate(
            int Id,
            string Name,
            string? Code,
            string? ProjectCategory,
            string? TechnicalCategory,
            bool IsBuild);

        private sealed record OverviewRowProjection(
            int ProjectId,
            int Year,
            string Project,
            string? ProjectCode,
            ProliferationSource Source,
            string DataType,
            string? UnitName,
            DateOnly? Date,
            int Quantity,
            ApprovalStatus ApprovalStatus,
            YearPreferenceMode? Mode);

        private sealed record CombinationKey(int ProjectId, ProliferationSource Source, int Year);

        private sealed record ProliferationGroupedDetailedEntryProjection(
            Guid Id,
            int ProjectId,
            ProliferationSource Source,
            int Year,
            DateOnly Date,
            string UnitName,
            int Quantity,
            string? Remarks);

    }
}
