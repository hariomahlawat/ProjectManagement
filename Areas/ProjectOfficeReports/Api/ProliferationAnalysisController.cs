using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Areas.ProjectOfficeReports.Application;

namespace ProjectManagement.Areas.ProjectOfficeReports.Api;

[ApiController]
[Route("api/proliferation/reports/analysis")]
public sealed class ProliferationAnalysisController : ControllerBase
{
    private readonly ProliferationAnalysisService _analysisService;

    public ProliferationAnalysisController(ProliferationAnalysisService analysisService)
    {
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
    }

    [HttpPost]
    [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<ProliferationAnalysisResultDto>> Run(
        [FromBody] ProliferationAnalysisRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _analysisService.RunAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(CreateValidationProblem(exception.Message));
        }
    }

    [HttpPost("export")]
    [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Export(
        [FromBody] ProliferationAnalysisRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var (content, fileName) = await _analysisService.ExportAsync(request, cancellationToken);
            return File(
                content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(CreateValidationProblem(exception.Message));
        }
    }

    private ProblemDetails CreateValidationProblem(string detail)
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "The report options are incomplete or invalid.",
            Detail = detail,
            Instance = HttpContext.Request.Path
        };
    }
}
