using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Security.Claims;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Api;

[ApiController]
[Route("api/proliferation/reports/analysis")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProliferationAnalysisController : ControllerBase
{
    private readonly ProliferationAnalysisService _analysisService;
    private readonly IAuditService _audit;
    private readonly ILogger<ProliferationAnalysisController> _logger;

    public ProliferationAnalysisController(
        ProliferationAnalysisService analysisService,
        IAuditService audit,
        ILogger<ProliferationAnalysisController> logger)
    {
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost]
    [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(typeof(ProliferationAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ProliferationAnalysisResultDto>> Run(
        [FromBody] ProliferationAnalysisRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _analysisService.RunAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (ProliferationAnalysisValidationException exception)
        {
            return BadRequest(CreateValidationProblem(exception.Message));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return CreateUnexpectedFailure(exception, "generate");
        }
    }

    [HttpPost("export")]
    [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Export(
        [FromBody] ProliferationAnalysisRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var (content, fileName) = await _analysisService.ExportAsync(
                request,
                User.Identity?.Name ?? "Unknown user",
                cancellationToken);

            await TryAuditExportAsync(request, fileName, cancellationToken);

            return File(
                content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
        catch (ProliferationAnalysisValidationException exception)
        {
            return BadRequest(CreateValidationProblem(exception.Message));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return CreateUnexpectedFailure(exception, "export");
        }
    }


    private async Task TryAuditExportAsync(
        ProliferationAnalysisRequestDto request,
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            await _audit.LogAsync(
                "Proliferation.Export",
                "Exported proliferation analysis workbook.",
                userId: User.FindFirstValue(ClaimTypes.NameIdentifier),
                userName: User.Identity?.Name,
                data: new Dictionary<string, string?>
                {
                    ["Scope"] = request.Scope.ToString(),
                    ["PeriodMode"] = request.PeriodMode.ToString(),
                    ["SelectedProjects"] = (request.ProjectIds?.Length ?? 0).ToString(CultureInfo.InvariantCulture),
                    ["Source"] = request.Source?.ToString() ?? "All",
                    ["IncludedUnitSummary"] = request.IncludeUnitBreakdown.ToString(),
                    ["FileName"] = fileName
                },
                http: HttpContext);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The proliferation workbook was generated, but its export audit could not be recorded. TraceId: {TraceId}",
                HttpContext.TraceIdentifier);
        }
    }

    private ProblemDetails CreateValidationProblem(string detail)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "The report options are incomplete or invalid.",
            Detail = detail,
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return problem;
    }

    private ObjectResult CreateUnexpectedFailure(Exception exception, string operation)
    {
        var traceId = HttpContext.TraceIdentifier;

        _logger.LogError(
            exception,
            "Unable to {Operation} proliferation analysis. TraceId: {TraceId}; Path: {Path}; User: {User}",
            operation,
            traceId,
            HttpContext.Request.Path,
            User.Identity?.Name ?? "anonymous");

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "The proliferation report could not be generated.",
            Detail = "An unexpected error occurred. Use the reference number below if the problem continues.",
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["traceId"] = traceId;

        return StatusCode(StatusCodes.Status500InternalServerError, problem);
    }
}
