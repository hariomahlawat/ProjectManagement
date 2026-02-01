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

namespace ProjectManagement.Areas.ProjectOfficeReports.Api
{
    [ApiController]
    [Route("api/proliferation")]
    [Authorize]
    public sealed class ProliferationController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ProliferationTrackerReadService _readSvc;
        private readonly ProliferationSubmissionService _submitSvc;
        private readonly ProliferationManageService _manageSvc;
        private readonly ProliferationOverviewService _overviewSvc;
        private readonly IProliferationExportService _exportService;
        private readonly ILogger<ProliferationController> _logger;

        public ProliferationController(
            ApplicationDbContext db,
            ProliferationTrackerReadService readSvc,
            ProliferationSubmissionService submitSvc,
            ProliferationManageService manageSvc,
            ProliferationOverviewService overviewSvc,
            IProliferationExportService exportService,
            ILogger<ProliferationController> logger)
        {
            _db = db;
            _readSvc = readSvc;
            _submitSvc = submitSvc;
            _manageSvc = manageSvc;
            _overviewSvc = overviewSvc;
            _exportService = exportService;
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
                    ApprovedOnUtc = item.ApprovedOnUtc
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
                    ModeLabel = item.Mode.ToString(),
                    EffectiveMode = item.EffectiveMode,
                    EffectiveModeLabel = item.EffectiveMode.ToString(),
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
                "Configured Mode",
                "Effective Mode",
                "Has Approved Yearly",
                "Has Approved Granular",
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
            var fileName = $"proliferation-preference-overrides-{timestamp}.csv";
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
                ApprovedOnUtc = detail.ApprovedOnUtc
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
                ApprovedOnUtc = detail.ApprovedOnUtc
            };
        }

        [HttpGet("projects")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
        public async Task<ActionResult<IReadOnlyList<ProliferationProjectLookupDto>>> GetEligibleProjects(
            [FromQuery] string? q,
            [FromQuery] int? projectCategoryId,
            [FromQuery] int? technicalCategoryId,
            CancellationToken ct)
        {
            var projects = _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Completed);

            if (projectCategoryId.HasValue)
            {
                projects = projects.Where(p => p.CategoryId == projectCategoryId.Value);
            }

            if (technicalCategoryId.HasValue)
            {
                projects = projects.Where(p => p.TechnicalCategoryId == technicalCategoryId.Value);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = $"%{EscapeLikePattern(q.Trim())}%";
                projects = projects.Where(p =>
                    EF.Functions.ILike(p.Name, term, "\\") ||
                    (p.CaseFileNumber != null && EF.Functions.ILike(p.CaseFileNumber, term, "\\")));
            }

            var results = await projects
                .OrderBy(p => p.Name)
                .Take(25)
                .Select(p => new ProliferationProjectLookupDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Code = p.CaseFileNumber
                })
                .ToListAsync(ct);

            return results;
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

        [HttpGet("overview")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
        public async Task<ActionResult<ProliferationOverviewDto>> GetOverview([FromQuery] ProliferationOverviewQuery q, CancellationToken ct)
        {
            var requestedPageSize = q.PageSize;
            var unpaged = requestedPageSize == 0;
            var page = q.Page < 1 ? 1 : q.Page;
            var pageSize = unpaged
                ? 0
                : (requestedPageSize < 1 ? 50 : Math.Min(requestedPageSize, 200));

            DateTime? from = q.FromDateUtc;
            DateTime? to = q.ToDateUtc;
            DateOnly? fromDateOnly = from.HasValue ? DateOnly.FromDateTime(from.Value) : null;
            DateOnly? toDateOnly = to.HasValue ? DateOnly.FromDateTime(to.Value) : null;

            var projectsQuery = _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && !p.IsArchived);

            if (q.ProjectId.HasValue)
            {
                projectsQuery = projectsQuery.Where(p => p.Id == q.ProjectId.Value);
            }

            if (q.ProjectCategoryId.HasValue)
            {
                projectsQuery = projectsQuery.Where(p => p.CategoryId == q.ProjectCategoryId.Value);
            }

            if (q.TechnicalCategoryId.HasValue)
            {
                projectsQuery = projectsQuery.Where(p => p.TechnicalCategoryId == q.TechnicalCategoryId.Value);
            }

            var yearlyBase = from y in _db.Set<ProliferationYearly>().AsNoTracking()
                             join p in projectsQuery on y.ProjectId equals p.Id
                             join pref in _db.Set<ProliferationYearPreference>().AsNoTracking()
                                 on new { y.ProjectId, y.Source, y.Year } equals new { pref.ProjectId, pref.Source, pref.Year } into prefJoin
                             from pref in prefJoin.DefaultIfEmpty()
                             where y.ApprovalStatus == ApprovalStatus.Approved
                             select new { Yearly = y, Project = p, Preference = pref };

            var granularBase = from g in _db.Set<ProliferationGranular>().AsNoTracking()
                               join p in projectsQuery on g.ProjectId equals p.Id
                               where g.ApprovalStatus == ApprovalStatus.Approved
                               select new { Granular = g, Project = p };

            var kind = ParseKind(q.Kind);

            if (fromDateOnly.HasValue && toDateOnly.HasValue)
            {
                yearlyBase = yearlyBase.Where(x => x.Yearly.Year >= fromDateOnly.Value.Year && x.Yearly.Year <= toDateOnly.Value.Year);
                granularBase = granularBase.Where(x => x.Granular.ProliferationDate >= fromDateOnly && x.Granular.ProliferationDate <= toDateOnly);
            }
            else if (q.Years is { Length: > 0 })
            {
                var years = q.Years.ToHashSet();
                yearlyBase = yearlyBase.Where(x => years.Contains(x.Yearly.Year));
                granularBase = granularBase.Where(x => years.Contains(x.Granular.ProliferationDate.Year));
            }

            if (q.Source.HasValue)
            {
                yearlyBase = yearlyBase.Where(x => x.Yearly.Source == q.Source);
                granularBase = granularBase.Where(x => x.Granular.Source == q.Source);
            }

            if (!string.IsNullOrWhiteSpace(q.Search))
            {
                var like = $"%{q.Search.Trim()}%";
                yearlyBase = yearlyBase.Where(x => EF.Functions.ILike(x.Project.Name, like));
                granularBase = granularBase.Where(x =>
                    EF.Functions.ILike(x.Project.Name, like) ||
                    EF.Functions.ILike(x.Granular.UnitName, like));
            }

            if (kind == ProliferationRecordKind.Yearly)
            {
                granularBase = granularBase.Where(_ => false);
            }
            else if (kind == ProliferationRecordKind.Granular)
            {
                yearlyBase = yearlyBase.Where(_ => false);
            }

            var yearlyRawQuery = yearlyBase.Select(x => new
            {
                ProjectId = x.Project.Id,
                Year = x.Yearly.Year,
                Project = x.Project.Name,
                ProjectCode = x.Project.CaseFileNumber,
                Source = x.Yearly.Source,
                DataType = "Yearly",
                UnitName = (string?)null,
                Date = (DateOnly?)null,
                Quantity = x.Yearly.TotalQuantity,
                ApprovalStatus = x.Yearly.ApprovalStatus,
                Mode = x.Preference != null ? (YearPreferenceMode?)x.Preference.Mode : null
            });

            var granularRawQuery = granularBase.Select(x => new
            {
                ProjectId = x.Project.Id,
                Year = x.Granular.ProliferationDate.Year,
                Project = x.Project.Name,
                ProjectCode = x.Project.CaseFileNumber,
                Source = x.Granular.Source,
                DataType = "Granular",
                UnitName = (string?)x.Granular.UnitName,
                Date = (DateOnly?)x.Granular.ProliferationDate,
                Quantity = x.Granular.Quantity,
                ApprovalStatus = x.Granular.ApprovalStatus,
                Mode = (YearPreferenceMode?)null
            });

            var combinedRowsQuery = yearlyRawQuery.Concat(granularRawQuery);

            var totalCount = await combinedRowsQuery.CountAsync(ct);

            if (!unpaged && pageSize == 0)
            {
                pageSize = 50;
            }

            if (!unpaged && pageSize > 0)
            {
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                if (totalPages > 0 && page > totalPages)
                {
                    page = totalPages;
                }
            }
            else if (unpaged)
            {
                page = 1;
            }

            var orderedRowsQuery = combinedRowsQuery
                .OrderByDescending(r => r.Year)
                .ThenBy(r => r.Project)
                .ThenBy(r => r.Source)
                .ThenBy(r => r.DataType)
                .ThenBy(r => r.Date);

            var pagedRowsQuery = orderedRowsQuery.AsQueryable();

            if (!unpaged && pageSize > 0)
            {
                pagedRowsQuery = pagedRowsQuery
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize);
            }

            var rowProjections = (await pagedRowsQuery.ToListAsync(ct))
                .Select(r => new OverviewRowProjection(
                    r.ProjectId,
                    r.Year,
                    r.Project,
                    r.ProjectCode,
                    r.Source,
                    r.DataType,
                    r.UnitName,
                    r.Date,
                    r.Quantity,
                    r.ApprovalStatus,
                    r.Mode))
                .ToList();

            var combinationKeys = rowProjections
                .Select(r => new CombinationKey(r.ProjectId, r.Source, r.Year))
                .Distinct()
                .ToList();

            var totalsLookup = new Dictionary<CombinationKey, int>(combinationKeys.Count);
            foreach (var combo in combinationKeys)
            {
                try
                {
                    var total = await _readSvc.GetEffectiveTotalAsync(combo.ProjectId, combo.Source, combo.Year, ct);
                    totalsLookup[combo] = total;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to compute effective proliferation total for project {ProjectId} ({Source}) in {Year}",
                        combo.ProjectId,
                        combo.Source,
                        combo.Year);
                }
            }

            var rows = rowProjections
                .Select(r =>
                {
                    totalsLookup.TryGetValue(new CombinationKey(r.ProjectId, r.Source, r.Year), out var effective);
                    return new ProliferationOverviewRowDto
                    {
                        ProjectId = r.ProjectId,
                        Year = r.Year,
                        Project = r.Project,
                        ProjectCode = r.ProjectCode,
                        Source = r.Source,
                        DataType = r.DataType,
                        UnitName = r.UnitName,
                        DateUtc = r.Date.HasValue
                            ? r.Date.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                            : null,
                        Quantity = r.Quantity,
                        EffectiveTotal = effective,
                        ApprovalStatus = r.ApprovalStatus.ToString(),
                        Mode = r.Mode?.ToString()
                    };
                })
                .ToList();

            var kpis = new ProliferationKpisDto();
            var completedProjectIds = await projectsQuery
                .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Completed)
                .Select(p => p.Id)
                .ToListAsync(ct);

            kpis.TotalCompletedProjects = completedProjectIds.Count;

            var yearlyCombosQuery = _db.Set<ProliferationYearly>().AsNoTracking()
                .Where(y => completedProjectIds.Contains(y.ProjectId) && y.ApprovalStatus == ApprovalStatus.Approved);

            var granularCombosQuery = _db.Set<ProliferationGranular>().AsNoTracking()
                .Where(g => completedProjectIds.Contains(g.ProjectId) && g.ApprovalStatus == ApprovalStatus.Approved);

            if (kind == ProliferationRecordKind.Yearly)
            {
                granularCombosQuery = granularCombosQuery.Where(_ => false);
            }
            else if (kind == ProliferationRecordKind.Granular)
            {
                yearlyCombosQuery = yearlyCombosQuery.Where(_ => false);
            }

            if (fromDateOnly.HasValue && toDateOnly.HasValue)
            {
                var rangeStartYear = fromDateOnly.Value.Year;
                var rangeEndYear = toDateOnly.Value.Year;
                yearlyCombosQuery = yearlyCombosQuery.Where(y => y.Year >= rangeStartYear && y.Year <= rangeEndYear);
                granularCombosQuery = granularCombosQuery.Where(g =>
                    g.ProliferationDate >= fromDateOnly.Value &&
                    g.ProliferationDate <= toDateOnly.Value);
            }
            else if (q.Years is { Length: > 0 })
            {
                var years = q.Years.ToHashSet();
                yearlyCombosQuery = yearlyCombosQuery.Where(y => years.Contains(y.Year));
                granularCombosQuery = granularCombosQuery.Where(g => years.Contains(g.ProliferationDate.Year));
            }

            if (q.Source.HasValue)
            {
                yearlyCombosQuery = yearlyCombosQuery.Where(y => y.Source == q.Source);
                granularCombosQuery = granularCombosQuery.Where(g => g.Source == q.Source);
            }

            var yearlyCombos = await yearlyCombosQuery
                .Select(y => new { y.ProjectId, y.Source, y.Year })
                .ToListAsync(ct);

            var granularCombos = await granularCombosQuery
                .Select(g => new { g.ProjectId, g.Source, Year = g.ProliferationDate.Year })
                .ToListAsync(ct);

            var projYears = yearlyCombos
                .Concat(granularCombos)
                .DistinctBy(x => new { x.ProjectId, x.Source, x.Year })
                .ToList();

            int totalAll = 0, totalSdd = 0, totalAbw = 0;
            foreach (var item in projYears)
            {
                int eff;
                try
                {
                    eff = await _readSvc.GetEffectiveTotalAsync(item.ProjectId, item.Source, item.Year, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to compute effective proliferation total for project {ProjectId} ({Source}) in {Year}",
                        item.ProjectId,
                        item.Source,
                        item.Year);
                    continue;
                }

                totalAll += eff;
                if (item.Source == ProliferationSource.Sdd)
                {
                    totalSdd += eff;
                }
                else
                {
                    totalAbw += eff;
                }
            }
            kpis.TotalProliferationAllTime = totalAll;
            kpis.TotalProliferationSdd = totalSdd;
            kpis.TotalProliferationAbw515 = totalAbw;

            var end = DateTime.UtcNow;
            var start = end.AddDays(-365);
            var fromCutoff = DateOnly.FromDateTime(start);
            var toCutoff = DateOnly.FromDateTime(end);

            var windowStartYear = start.Year;
            var windowEndYear = end.Year;
            var yearCount = windowEndYear - windowStartYear + 1;
            var windowYears = yearCount > 0
                ? Enumerable.Range(windowStartYear, yearCount).ToArray()
                : Array.Empty<int>();

            var lastYearCombos = new HashSet<CombinationKey>();

            if (completedProjectIds.Count > 0 && windowYears.Length > 0)
            {
                var lastYearlyQuery = _db.Set<ProliferationYearly>().AsNoTracking()
                    .Where(y =>
                        y.ApprovalStatus == ApprovalStatus.Approved &&
                        completedProjectIds.Contains(y.ProjectId) &&
                        windowYears.Contains(y.Year));

                if (q.Source.HasValue)
                {
                    lastYearlyQuery = lastYearlyQuery.Where(y => y.Source == q.Source.Value);
                }

                var lastYearlyCombos = await lastYearlyQuery
                    .Select(y => new { y.ProjectId, y.Source, y.Year })
                    .ToListAsync(ct);

                foreach (var item in lastYearlyCombos)
                {
                    lastYearCombos.Add(new CombinationKey(item.ProjectId, item.Source, item.Year));
                }

                var lastGranularQuery = _db.Set<ProliferationGranular>().AsNoTracking()
                    .Where(g =>
                        g.ApprovalStatus == ApprovalStatus.Approved &&
                        completedProjectIds.Contains(g.ProjectId) &&
                        g.ProliferationDate >= fromCutoff &&
                        g.ProliferationDate <= toCutoff);

                if (q.Source.HasValue)
                {
                    lastGranularQuery = lastGranularQuery.Where(g => g.Source == q.Source.Value);
                }

                var lastGranularCombos = await lastGranularQuery
                    .Select(g => new { g.ProjectId, g.Source, Year = g.ProliferationDate.Year })
                    .ToListAsync(ct);

                foreach (var item in lastGranularCombos)
                {
                    lastYearCombos.Add(new CombinationKey(item.ProjectId, item.Source, item.Year));
                }
            }

            int lastYearTotal = 0, lastYearSdd = 0, lastYearAbw = 0;
            var proliferatedProjects = new HashSet<int>();

            foreach (var combo in lastYearCombos)
            {
                int eff;
                try
                {
                    eff = await _readSvc.GetEffectiveTotalAsync(combo.ProjectId, combo.Source, combo.Year, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to compute effective proliferation total for project {ProjectId} ({Source}) in {Year}",
                        combo.ProjectId,
                        combo.Source,
                        combo.Year);
                    continue;
                }

                lastYearTotal += eff;
                if (combo.Source == ProliferationSource.Sdd)
                {
                    lastYearSdd += eff;
                }
                else
                {
                    lastYearAbw += eff;
                }

                if (eff > 0)
                {
                    proliferatedProjects.Add(combo.ProjectId);
                }
            }

            kpis.LastYearTotalProliferation = lastYearTotal;
            kpis.LastYearSdd = lastYearSdd;
            kpis.LastYearAbw515 = lastYearAbw;
            kpis.LastYearProjectsProliferated = proliferatedProjects.Count;

            var payload = new ProliferationOverviewDto
            {
                Kpis = kpis,
                TotalCount = totalCount,
                Page = page,
                PageSize = unpaged ? rows.Count : pageSize,
                Rows = rows
            };
            return Ok(payload);
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
            return result.Success ? Ok() : BadRequest(result.Error);
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
                return result.Success ? Ok() : BadRequest(result.Error);
            }
            catch (DbUpdateException ex)
            {
                var root = ex.GetBaseException();
                _logger.LogError(ex, "Granular save failed. {Message}", root.Message);

                return Problem(
                    title: "Database update failed",
                    detail: root.Message,
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

            var result = await _submitSvc.DecideYearlyAsync(id, dto.Approve, dto.RowVersion, User, ct);
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

            var result = await _submitSvc.DecideGranularAsync(id, dto.Approve, dto.RowVersion, User, ct);
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
            if (dto.Source == ProliferationSource.Abw515 && dto.Mode != YearPreferenceMode.Auto)
                return BadRequest("ABW 515 uses Yearly totals and cannot be overridden");

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
                return noContent ? NoContent() : Ok();
            }

            if (string.Equals(result.Error, "Record not found.", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound();
            }

            return BadRequest(result.Error);
        }

        private static string CsvEscape(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var needsEscaping = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            if (!needsEscaping)
            {
                return value;
            }

            var escaped = value.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

        // SECTION: Search helpers
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

    }
}
