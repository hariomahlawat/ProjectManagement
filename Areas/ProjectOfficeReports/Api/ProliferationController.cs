using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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

        public ProliferationController(
            ApplicationDbContext db,
            ProliferationTrackerReadService readSvc,
            ProliferationSubmissionService submitSvc)
        {
            _db = db;
            _readSvc = readSvc;
            _submitSvc = submitSvc;
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

            var yearlyCombos = await _db.Set<ProliferationYearly>().AsNoTracking()
                .Where(y => completedProjectIds.Contains(y.ProjectId) && y.ApprovalStatus == ApprovalStatus.Approved)
                .Select(y => new { y.ProjectId, y.Source, y.Year })
                .ToListAsync(ct);

            var granularCombos = await _db.Set<ProliferationGranular>().AsNoTracking()
                .Where(g => completedProjectIds.Contains(g.ProjectId) && g.ApprovalStatus == ApprovalStatus.Approved)
                .Select(g => new { g.ProjectId, g.Source, Year = g.ProliferationDate.Year })
                .ToListAsync(ct);

            var projYears = yearlyCombos
                .Concat(granularCombos)
                .DistinctBy(x => new { x.ProjectId, x.Source, x.Year })
                .ToList();

            int totalAll = 0, totalSdd = 0, totalAbw = 0;
            foreach (var item in projYears)
            {
                var eff = await _readSvc.GetEffectiveTotalAsync(item.ProjectId, item.Source, item.Year, ct);
                totalAll += eff;
                if (item.Source == ProliferationSource.Sdd) totalSdd += eff; else totalAbw += eff;
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
            kpis.LastYearAbw515 = 0;
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
                var date = r.DateUtc?.ToString("yyyy-MM-dd") ?? "";
                var mode = r.Mode ?? "";
                sb.AppendLine($"{r.Year},\"{r.Project}\",{r.Source},{r.DataType},\"{r.UnitName}\",\"{r.SimulatorName}\",{date},{r.Quantity},{r.ApprovalStatus},{mode}");
            }
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", "proliferation-export.csv");
        }
    }
}
