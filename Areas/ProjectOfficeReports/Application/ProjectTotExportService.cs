using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Services;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public interface IProjectTotExportService
{
    Task<ProjectTotExportResult> ExportAsync(ProjectTotExportRequest request, CancellationToken cancellationToken);
}

public sealed class ProjectTotExportService : IProjectTotExportService
{
    private readonly ProjectTotTrackerReadService _trackerService;
    private readonly IProjectTotExcelWorkbookBuilder _workbookBuilder;
    private readonly IClock _clock;

    public ProjectTotExportService(
        ProjectTotTrackerReadService trackerService,
        IProjectTotExcelWorkbookBuilder workbookBuilder,
        IClock clock)
    {
        _trackerService = trackerService ?? throw new ArgumentNullException(nameof(trackerService));
        _workbookBuilder = workbookBuilder ?? throw new ArgumentNullException(nameof(workbookBuilder));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ProjectTotExportResult> ExportAsync(ProjectTotExportRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (!validation.Success)
        {
            return ProjectTotExportResult.Failure(validation.Error ?? "Export failed.");
        }

        var filter = validation.Filter!;
        var rows = await _trackerService.GetAsync(filter, cancellationToken);
        var generatedAt = _clock.UtcNow;

        var content = _workbookBuilder.Build(new ProjectTotExcelWorkbookContext(rows, generatedAt, filter));
        var fileName = BuildFileName(generatedAt);
        var file = new ProjectTotExportFile(fileName, content, ProjectTotExportFile.ExcelContentType);

        return ProjectTotExportResult.FromFile(file);
    }

    private static string BuildFileName(DateTimeOffset generatedAtUtc)
    {
        return $"tot-export-{generatedAtUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}.xlsx";
    }

    private static ValidationResult Validate(ProjectTotExportRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
        {
            return ValidationResult.Fail("The requesting user could not be determined.");
        }

        if (request.StartedFrom.HasValue && request.StartedTo.HasValue && request.StartedFrom > request.StartedTo)
        {
            return ValidationResult.Fail("The ToT start date range is invalid. The start date must be on or before the end date.");
        }

        if (request.CompletedFrom.HasValue && request.CompletedTo.HasValue && request.CompletedFrom > request.CompletedTo)
        {
            return ValidationResult.Fail("The completion date range is invalid. The start date must be on or before the end date.");
        }

        var normalizedSearchTerm = string.IsNullOrWhiteSpace(request.SearchTerm)
            ? null
            : request.SearchTerm.Trim();

        var filter = new ProjectTotTrackerFilter
        {
            TotStatus = request.TotStatus,
            RequestState = request.OnlyPendingRequests ? null : request.RequestState,
            OnlyPendingRequests = request.OnlyPendingRequests,
            SearchTerm = normalizedSearchTerm,
            StartedFrom = request.StartedFrom,
            StartedTo = request.StartedTo,
            CompletedFrom = request.CompletedFrom,
            CompletedTo = request.CompletedTo
        };

        return ValidationResult.Success(filter);
    }

    private sealed record ValidationResult(bool Success, string? Error, ProjectTotTrackerFilter? Filter)
    {
        public static ValidationResult Fail(string error) => new(false, error, null);

        public static ValidationResult Success(ProjectTotTrackerFilter filter) => new(true, null, filter);
    }
}

public sealed record ProjectTotExportRequest(
    ProjectTotStatus? TotStatus,
    ProjectTotRequestDecisionState? RequestState,
    bool OnlyPendingRequests,
    DateOnly? StartedFrom,
    DateOnly? StartedTo,
    DateOnly? CompletedFrom,
    DateOnly? CompletedTo,
    string? SearchTerm,
    string RequestedByUserId);

public sealed record ProjectTotExportFile(string FileName, byte[] Content, string ContentType)
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

public sealed record ProjectTotExportResult(bool Success, ProjectTotExportFile? File, IReadOnlyList<string> Errors)
{
    public static ProjectTotExportResult FromFile(ProjectTotExportFile file) => new(true, file, Array.Empty<string>());

    public static ProjectTotExportResult Failure(params string[] errors)
        => new(false, null, errors.Length == 0 ? new[] { "Export failed." } : errors);
}
