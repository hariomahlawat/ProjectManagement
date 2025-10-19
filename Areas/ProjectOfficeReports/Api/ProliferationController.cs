using System;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Areas.ProjectOfficeReports.Api;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
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
        private readonly ILogger<ProliferationController> _logger;

        public ProliferationController(
            ApplicationDbContext db,
            ProliferationTrackerReadService readSvc,
            ProliferationSubmissionService submitSvc,
            ILogger<ProliferationController> logger)
        {
            _db = db;
            _readSvc = readSvc;
            _submitSvc = submitSvc;
            _logger = logger;
        }

        [HttpGet("overview")]
        public async Task<ActionResult<ProliferationOverviewDto>> GetOverview([FromQuery] ProliferationOverviewQuery q, CancellationToken ct)
        {
            var page = q.Page < 1 ? 1 : q.Page;
            var pageSize = q.PageSize < 1 ? 50 : Math.Min(q.PageSize, 200);

            DateTime? from = q.FromDateUtc;
            DateTime? to = q.ToDateUtc;
            DateOnly? fromDateOnly = from.HasValue ? DateOnly.FromDateTime(from.Value) : null;
            DateOnly? toDateOnly = to.HasValue ? DateOnly.FromDateTime(to.Value) : null;

            var projectsQuery = _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && !p.IsArchived);

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
                    EF.Functions.ILike(x.Granular.SimulatorName, like) ||
                    EF.Functions.ILike(x.Granular.UnitName, like));
            }

            var yearlyRowsQuery = yearlyBase.Select(x => new ProliferationOverviewRowDto
            {
                Year = x.Yearly.Year,
                Project = x.Project.Name,
                ProjectCode = x.Project.CaseFileNumber,
                Source = x.Yearly.Source,
                DataType = "Yearly",
                UnitName = null,
                SimulatorName = null,
                DateUtc = null,
                Quantity = x.Yearly.TotalQuantity,
                ApprovalStatus = x.Yearly.ApprovalStatus.ToString(),
                Mode = x.Preference != null ? x.Preference.Mode.ToString() : null
            });

            var granularRowsQuery = granularBase.Select(x => new ProliferationOverviewRowDto
            {
                Year = x.Granular.ProliferationDate.Year,
                Project = x.Project.Name,
                ProjectCode = x.Project.CaseFileNumber,
                Source = x.Granular.Source,
                DataType = "Granular",
                UnitName = x.Granular.UnitName,
                SimulatorName = x.Granular.SimulatorName,
                DateUtc = x.Granular.ProliferationDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Quantity = x.Granular.Quantity,
                ApprovalStatus = x.Granular.ApprovalStatus.ToString(),
                Mode = null
            });

            var combinedRows = yearlyRowsQuery.Concat(granularRowsQuery);

            var totalCount = await combinedRows.CountAsync(ct);
            var rows = await combinedRows
                .OrderByDescending(r => r.Year)
                .ThenBy(r => r.Project)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var kpis = new ProliferationKpisDto();
            var completedProjectIds = await projectsQuery
                .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Completed)
                .Select(p => p.Id)
                .ToListAsync(ct);

            kpis.TotalCompletedProjects = completedProjectIds.Count;

            var yearlyCombosQuery = _db.Set<ProliferationYearly>().AsNoTracking()
                .Where(y => completedProjectIds.Contains(y.ProjectId) && y.ApprovalStatus == ApprovalStatus.Approved);

            var granularCombosQuery = _db.ProliferationGranularYearlyView.AsNoTracking()
                .Where(g => completedProjectIds.Contains(g.ProjectId));

            if (fromDateOnly.HasValue && toDateOnly.HasValue)
            {
                var startYear = fromDateOnly.Value.Year;
                var endYear = toDateOnly.Value.Year;
                yearlyCombosQuery = yearlyCombosQuery.Where(y => y.Year >= startYear && y.Year <= endYear);
                granularCombosQuery = granularCombosQuery.Where(g => g.Year >= startYear && g.Year <= endYear);
            }
            else if (q.Years is { Length: > 0 })
            {
                var years = q.Years.ToHashSet();
                yearlyCombosQuery = yearlyCombosQuery.Where(y => years.Contains(y.Year));
                granularCombosQuery = granularCombosQuery.Where(g => years.Contains(g.Year));
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
                .Select(g => new { g.ProjectId, g.Source, g.Year })
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

            var last12Granular = await _db.Set<ProliferationGranular>().AsNoTracking()
                .Where(g => g.ApprovalStatus == ApprovalStatus.Approved &&
                            g.ProliferationDate >= fromCutoff &&
                            g.ProliferationDate <= toCutoff)
                .ToListAsync(ct);

            kpis.LastYearTotalProliferation = last12Granular.Sum(g => g.Quantity);
            kpis.LastYearSdd = last12Granular.Where(g => g.Source == ProliferationSource.Sdd).Sum(g => g.Quantity);
            kpis.LastYearAbw515 = last12Granular.Where(g => g.Source == ProliferationSource.Abw515).Sum(g => g.Quantity);
            kpis.LastYearProjectsProliferated = last12Granular.Select(g => g.ProjectId).Distinct().Count();

            var payload = new ProliferationOverviewDto { Kpis = kpis, TotalCount = totalCount, Rows = rows };
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

            var result = await _submitSvc.CreateGranularAsync(dto, User, ct);
            return result.Success ? Ok() : BadRequest(result.Error);
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

        [HttpPost("import/yearly")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.SubmitProliferationTracker)]
        public async Task<ActionResult<ImportResultDto>> ImportYearly([FromForm] IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0) return BadRequest("File required");
            using var stream = file.OpenReadStream();
            var result = await _submitSvc.ImportYearlyCsvAsync(stream, User, ct);
            return Ok(result);
        }

        [HttpPost("import/granular")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.SubmitProliferationTracker)]
        public async Task<ActionResult<ImportResultDto>> ImportGranular([FromForm] IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0) return BadRequest("File required");
            using var stream = file.OpenReadStream();
            var result = await _submitSvc.ImportGranularCsvAsync(stream, User, ct);
            return Ok(result);
        }

        [HttpGet("export")]
        public async Task<IActionResult> Export([FromQuery] ProliferationOverviewQuery q, CancellationToken ct)
        {
            var overview = await GetOverview(q, ct);
            if (overview.Result is not OkObjectResult ok) return overview.Result!;
            var payload = (ProliferationOverviewDto)ok.Value!;

            var sb = new StringBuilder();
            sb.AppendLine("Year,Project,Source,DataType,UnitName,SimulatorName,Date,Quantity,ApprovalStatus,Mode");
            foreach (var r in payload.Rows)
            {
                var date = r.DateUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
                var mode = r.Mode ?? string.Empty;
                AppendCsvRow(
                    sb,
                    r.Year.ToString(CultureInfo.InvariantCulture),
                    r.Project,
                    r.Source.ToString(),
                    r.DataType,
                    r.UnitName,
                    r.SimulatorName,
                    date,
                    r.Quantity.ToString(CultureInfo.InvariantCulture),
                    r.ApprovalStatus,
                    mode);
            }
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", "proliferation-export.csv");
        }

        private static void AppendCsvRow(StringBuilder sb, params string?[] values)
        {
            if (values == null || values.Length == 0)
            {
                sb.AppendLine();
                return;
            }

            for (var i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(EscapeCsv(values[i]));
            }

            sb.AppendLine();
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sanitized = value.Replace("\"", "\"\"");
            var needsQuotes = sanitized.IndexOfAny(new[] { ',', '\n', '\r', '"' }) >= 0;
            return needsQuotes ? $"\"{sanitized}\"" : sanitized;
        }
    }
}
