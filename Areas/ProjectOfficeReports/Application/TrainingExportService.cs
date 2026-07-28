using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application.Training.Dtos;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;
using ProjectManagement.Services;
using ProjectManagement.Utilities;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public interface ITrainingExportService
{
    Task<TrainingExportResult> ExportAsync(TrainingExportRequest request, CancellationToken cancellationToken);
}

public sealed class TrainingExportService : ITrainingExportService
{
    private readonly TrainingTrackerReadService _readService;
    private readonly ITrainingExcelWorkbookBuilder _workbookBuilder;
    private readonly IClock _clock;
    private readonly IOptionsSnapshot<TrainingTrackerOptions> _options;
    private readonly IAuditService _audit;
    private readonly ILogger<TrainingExportService> _logger;

    public TrainingExportService(
        TrainingTrackerReadService readService,
        ITrainingExcelWorkbookBuilder workbookBuilder,
        IClock clock,
        IOptionsSnapshot<TrainingTrackerOptions> options,
        IAuditService audit,
        ILogger<TrainingExportService> logger)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _workbookBuilder = workbookBuilder ?? throw new ArgumentNullException(nameof(workbookBuilder));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TrainingExportResult> ExportAsync(TrainingExportRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var validationError = Validate(request);
        if (validationError is not null)
        {
            return TrainingExportResult.Failure(validationError);
        }

        var limits = ResolveLimits();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(limits.Timeout);
        var exportToken = timeoutSource.Token;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var normalizedSearch = string.IsNullOrWhiteSpace(request.Search)
                ? null
                : request.Search.Trim();

            var query = new TrainingTrackerQuery
            {
                ProjectId = request.ProjectId,
                ProjectTechnicalCategoryId = request.ProjectTechnicalCategoryId,
                From = request.From,
                To = request.To,
                Search = normalizedSearch,
                Category = request.Category
            };

            if (request.TrainingTypeId is { } typeId && typeId != Guid.Empty)
            {
                query.TrainingTypeIds.Add(typeId);
            }

            var rosterCategory = request.IncludeRoster
                && request.RosterScope == TrainingRosterScope.SelectedTraineeCategoryOnly
                ? request.Category
                : null;

            var dataset = await _readService.ExportAsync(query, request.IncludeRoster, rosterCategory, exportToken);

            if (dataset.Trainings.Count > limits.MaxTrainingRows)
            {
                return TrainingExportResult.Failure(
                    $"The selected export contains {dataset.Trainings.Count:N0} training records. " +
                    $"The current limit is {limits.MaxTrainingRows:N0}. Narrow the date range or other filters and try again.");
            }

            if (dataset.RosterRowCount > limits.MaxRosterRows)
            {
                return TrainingExportResult.Failure(
                    $"The selected export contains {dataset.RosterRowCount:N0} roster rows. " +
                    $"The current limit is {limits.MaxRosterRows:N0}. Narrow the date range or other filters and try again.");
            }

            var metadata = await ResolveMetadataAsync(request, exportToken);
            var kpis = await _readService.GetKpisAsync(query, exportToken);
            var generatedAt = _clock.UtcNow;

            // ClosedXML workbook creation is synchronous. Check the elapsed time
            // immediately before and after it so an overlong request still fails
            // with a controlled response instead of leaving the UI busy.
            ThrowIfExportTimedOut(stopwatch, limits.Timeout, exportToken);

            var content = _workbookBuilder.Build(new TrainingExcelWorkbookContext(
                dataset,
                kpis,
                generatedAt,
                request.RequestedByDisplayName,
                request.ApplicationBaseUrl,
                query.From,
                query.To,
                query.Search,
                request.IncludeRoster,
                request.RosterScope,
                metadata.TrainingTypeName,
                metadata.CategoryDisplayName,
                metadata.ProjectName,
                metadata.TechnicalCategoryDisplayName));

            ThrowIfExportTimedOut(stopwatch, limits.Timeout, exportToken);

            var fileName = BuildFileName(generatedAt);
            var file = new TrainingExportFile(fileName, content, TrainingExportFile.ExcelContentType);

            await Audit.Events.TrainingExportGenerated(
                    request.RequestedByUserId,
                    request.TrainingTypeId,
                    request.ProjectId,
                    request.ProjectTechnicalCategoryId,
                    request.Category,
                    request.From,
                    request.To,
                    normalizedSearch,
                    request.IncludeRoster,
                    request.RosterScope.ToString(),
                    dataset.Trainings.Count,
                    dataset.RosterRowCount,
                    fileName)
                .WriteAsync(_audit, userName: request.RequestedByDisplayName);

            _logger.LogInformation(
                "Generated training export {FileName} with {TrainingCount} training rows and {RosterCount} roster rows for user {UserId} in {ElapsedMs} ms",
                fileName,
                dataset.Trainings.Count,
                dataset.RosterRowCount,
                request.RequestedByUserId,
                stopwatch.ElapsedMilliseconds);

            return TrainingExportResult.FromFile(file);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Training export timed out after {ElapsedMs} ms for user {UserId}",
                stopwatch.ElapsedMilliseconds,
                request.RequestedByUserId);

            return TrainingExportResult.Failure(
                "The export took too long to prepare. Narrow the date range or other filters and try again.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Training export failed for user {UserId} after {ElapsedMs} ms",
                request.RequestedByUserId,
                stopwatch.ElapsedMilliseconds);

            return TrainingExportResult.Failure(
                "The export could not be generated. Please retry, or narrow the selected filters if the problem continues.");
        }
    }

    private static void ThrowIfExportTimedOut(
        Stopwatch stopwatch,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stopwatch.Elapsed > timeout)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private static string? Validate(TrainingExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
        {
            return "The requesting user could not be determined.";
        }

        if (request.From.HasValue && request.To.HasValue && request.From > request.To)
        {
            return "The start date must be on or before the end date.";
        }

        if (request.Category.HasValue && !Enum.IsDefined(request.Category.Value))
        {
            return "The selected trainee category is not valid.";
        }

        if (!string.IsNullOrWhiteSpace(request.Search) && request.Search.Trim().Length > 200)
        {
            return "Search text cannot exceed 200 characters.";
        }

        if (!Enum.IsDefined(request.RosterScope))
        {
            return "The selected roster scope is not valid.";
        }

        if (request.IncludeRoster
            && request.RosterScope == TrainingRosterScope.SelectedTraineeCategoryOnly
            && !request.Category.HasValue)
        {
            return "Select a trainee category before limiting roster rows to that category.";
        }

        return null;
    }

    private ExportLimits ResolveLimits()
    {
        var configured = _options.Value;
        return new ExportLimits(
            Math.Max(1, configured.MaxExportTrainingRows),
            Math.Max(1, configured.MaxExportRosterRows),
            TimeSpan.FromSeconds(Math.Max(10, configured.ExportTimeoutSeconds)));
    }

    private async Task<ExportMetadata> ResolveMetadataAsync(
        TrainingExportRequest request,
        CancellationToken cancellationToken)
    {
        string? trainingTypeName = null;
        if (request.TrainingTypeId is { } typeId && typeId != Guid.Empty)
        {
            var trainingTypes = await _readService.GetTrainingTypesAsync(cancellationToken);
            trainingTypeName = trainingTypes.FirstOrDefault(option => option.Id == typeId)?.Name;
        }

        string? projectName = null;
        if (request.ProjectId is { } projectId)
        {
            var projects = await _readService.GetProjectOptionsAsync(new[] { projectId }, cancellationToken);
            projectName = projects.FirstOrDefault(project => project.Id == projectId)?.Name;
        }

        string? technicalCategoryDisplayName = null;
        if (request.ProjectTechnicalCategoryId is { } technicalCategoryId)
        {
            var categories = await _readService.GetProjectTechnicalCategoryOptionsAsync(cancellationToken);
            technicalCategoryDisplayName = BuildTechnicalCategoryDisplayName(categories, technicalCategoryId);
        }

        return new ExportMetadata(
            trainingTypeName,
            GetCategoryDisplayName(request.Category),
            projectName,
            technicalCategoryDisplayName);
    }

    private static string BuildFileName(DateTimeOffset generatedAtUtc)
    {
        var generatedAtIst = TimeZoneInfo.ConvertTime(generatedAtUtc, TimeZoneHelper.GetIst());
        return $"training-tracker-{generatedAtIst.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-IST.xlsx";
    }

    private static string? GetCategoryDisplayName(TrainingCategory? category)
        => category switch
        {
            TrainingCategory.Officer => "Officers",
            TrainingCategory.JuniorCommissionedOfficer => "Junior Commissioned Officers",
            TrainingCategory.OtherRank => "Other Ranks",
            _ => null
        };

    private static string? BuildTechnicalCategoryDisplayName(
        IReadOnlyList<ProjectTechnicalCategoryOption> categories,
        int selectedId)
    {
        var byId = categories.ToDictionary(category => category.Id);
        if (!byId.TryGetValue(selectedId, out var selected))
        {
            return null;
        }

        var names = new Stack<string>();
        var current = selected;
        var guard = new HashSet<int>();

        while (guard.Add(current.Id))
        {
            names.Push(current.Name);
            if (!current.ParentId.HasValue || !byId.TryGetValue(current.ParentId.Value, out var parent))
            {
                break;
            }

            current = parent;
        }

        var display = string.Join(" > ", names);
        return selected.IsActive ? display : $"{display} (inactive)";
    }

    private sealed record ExportMetadata(
        string? TrainingTypeName,
        string? CategoryDisplayName,
        string? ProjectName,
        string? TechnicalCategoryDisplayName);

    private sealed record ExportLimits(int MaxTrainingRows, int MaxRosterRows, TimeSpan Timeout);
}

public enum TrainingRosterScope
{
    AllTraineesInMatchingEvents = 0,
    SelectedTraineeCategoryOnly = 1
}

public sealed record TrainingExportRequest(
    Guid? TrainingTypeId,
    TrainingCategory? Category,
    int? ProjectId,
    int? ProjectTechnicalCategoryId,
    DateOnly? From,
    DateOnly? To,
    string? Search,
    bool IncludeRoster,
    TrainingRosterScope RosterScope,
    string RequestedByUserId,
    string RequestedByDisplayName,
    string ApplicationBaseUrl);

public sealed record TrainingExportFile(string FileName, byte[] Content, string ContentType)
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

public sealed record TrainingExportResult(bool Success, TrainingExportFile? File, IReadOnlyList<string> Errors)
{
    public static TrainingExportResult FromFile(TrainingExportFile file) => new(true, file, Array.Empty<string>());

    public static TrainingExportResult Failure(params string[] errors)
        => new(false, null, errors.Length == 0 ? new[] { "Export failed." } : errors);
}
