using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Areas.ProjectOfficeReports.Application;

namespace ProjectManagement.Areas.ProjectOfficeReports.Api
{
    // SECTION: Proliferation reports endpoints
    // Report runs and exports live in ProliferationAnalysisController
    // (api/proliferation/reports/analysis).
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
    }
}
