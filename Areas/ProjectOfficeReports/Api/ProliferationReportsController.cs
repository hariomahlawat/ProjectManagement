using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Areas.ProjectOfficeReports.Application;

namespace ProjectManagement.Areas.ProjectOfficeReports.Api
{
    // SECTION: Proliferation reports endpoints
    [ApiController]
    [Route("api/proliferation/reports")]
    public sealed class ProliferationReportsController : ControllerBase
    {
        private readonly ProliferationReportsService _svc;

        public ProliferationReportsController(ProliferationReportsService svc)
        {
            _svc = svc;
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
            var result = await _svc.RunAsync(q, ct);
            return Ok(result);
        }

        // SECTION: Export
        [HttpGet("export")]
        [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
        public async Task<IActionResult> Export([FromQuery] ProliferationReportQueryDto q, CancellationToken ct)
        {
            var (content, fileName) = await _svc.ExportAsync(q, ct);
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
    }
}
