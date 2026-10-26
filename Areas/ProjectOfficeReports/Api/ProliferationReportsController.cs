using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Api
{
    // SECTION: Proliferation reports endpoints
    [ApiController]
    [Route("api/proliferation/reports")]
    public sealed class ProliferationReportsController : ControllerBase
    {
        private readonly ProliferationReportsService _svc;
        private readonly IAuthorizationService _authorization;
        private readonly IAuditService _audit;

        public ProliferationReportsController(
            ProliferationReportsService svc,
            IAuthorizationService authorization,
            IAuditService audit)
        {
            _svc = svc;
            _authorization = authorization;
            _audit = audit;
        }

        // SECTION: Unit suggestions
        [HttpGet("unit-suggestions")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
        public async Task<ActionResult<IReadOnlyList<string>>> GetUnitSuggestions(
            [FromQuery] string? q,
            [FromQuery] int take = 25,
            CancellationToken ct = default)
        {
            var results = await _svc.GetUnitSuggestionsAsync(q, take, ct);
            return Ok(results);
        }

        // SECTION: Report run
        [HttpGet("run")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
        public async Task<ActionResult<ProliferationReportPageDto>> Run([FromQuery] ProliferationReportQueryDto q, CancellationToken ct)
        {
            if (!await CanReadRequestedRecordsAsync(q))
            {
                return Forbid();
            }

            try
            {
                var result = await _svc.RunAsync(q, ct);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // SECTION: Export
        [HttpGet("export")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
        public async Task<IActionResult> Export([FromQuery] ProliferationReportQueryDto q, CancellationToken ct)
        {
            if (!await CanReadRequestedRecordsAsync(q))
            {
                return Forbid();
            }

            try
            {
                var (content, fileName) = await _svc.ExportAsync(q, ct);

                await Audit.Events
                    .ProliferationReportExported(
                        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                        q.Report.ToString(),
                        ProliferationReportsService.NormalizeApprovalStatus(q.ApprovalStatus),
                        q.ProjectId,
                        fileName)
                    .WriteAsync(_audit, userName: User.Identity?.Name);

                return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Approved figures are open to every viewer. Pending and rejected records are
        // working data, readable elsewhere only by those who may submit proliferation.
        private async Task<bool> CanReadRequestedRecordsAsync(ProliferationReportQueryDto q)
        {
            if (!ProliferationReportsService.RequestsUnapprovedRecords(q.ApprovalStatus))
            {
                return true;
            }

            var result = await _authorization.AuthorizeAsync(User, ProjectOfficeReportsPolicies.SubmitProliferationTracker);
            return result.Succeeded;
        }
    }
}
