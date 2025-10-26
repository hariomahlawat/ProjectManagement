using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Services;
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

    public TrainingExportService(
        TrainingTrackerReadService readService,
        ITrainingExcelWorkbookBuilder workbookBuilder,
        IClock clock)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _workbookBuilder = workbookBuilder ?? throw new ArgumentNullException(nameof(workbookBuilder));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<TrainingExportResult> ExportAsync(TrainingExportRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
        {
            return TrainingExportResult.Failure("The requesting user could not be determined.");
        }

        if (request.From.HasValue && request.To.HasValue && request.From > request.To)
        {
            return TrainingExportResult.Failure("The start date must be on or before the end date.");
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(request.Search)
            ? null
            : request.Search.Trim();

        var query = new TrainingTrackerQuery
        {
            ProjectTechnicalCategoryId = request.ProjectTechnicalCategoryId,
            From = request.From,
            To = request.To,
            Search = normalizedSearch
        };

        if (request.Category.HasValue)
        {
            query.Category = request.Category.Value;
        }

        if (request.TrainingTypeId is { } typeId && typeId != Guid.Empty)
        {
            query.TrainingTypeIds.Add(typeId);
        }

        var metadata = await ResolveMetadataAsync(request, cancellationToken);
        var rows = await _readService.ExportAsync(query, request.IncludeRoster, cancellationToken);
        var generatedAt = _clock.UtcNow;

        var content = _workbookBuilder.Build(new TrainingExcelWorkbookContext(
            rows,
            generatedAt,
            query.From,
            query.To,
            query.Search,
            request.IncludeRoster,
            metadata.TrainingTypeName,
            metadata.CategoryDisplayName,
            metadata.TechnicalCategoryName,
            metadata.TechnicalCategoryDisplayName));

        var file = new TrainingExportFile(
            BuildFileName(generatedAt),
            content,
            TrainingExportFile.ExcelContentType);

        return TrainingExportResult.FromFile(file);
    }

    private async Task<ExportMetadata> ResolveMetadataAsync(TrainingExportRequest request, CancellationToken cancellationToken)
    {
        string? trainingTypeName = null;
        if (request.TrainingTypeId is { } typeId && typeId != Guid.Empty)
        {
            var trainingTypes = await _readService.GetTrainingTypesAsync(cancellationToken);
            trainingTypeName = trainingTypes.FirstOrDefault(option => option.Id == typeId)?.Name;
        }

        string? technicalCategoryName = null;
        string? technicalCategoryDisplayName = null;
        if (request.ProjectTechnicalCategoryId is { } technicalCategoryId)
        {
            var categories = await _readService.GetProjectTechnicalCategoryOptionsAsync(cancellationToken);
            var selected = categories.FirstOrDefault(option => option.Id == technicalCategoryId);
            if (selected is not null)
            {
                technicalCategoryName = selected.Name;
                technicalCategoryDisplayName = BuildTechnicalCategoryDisplayName(categories, selected.Id);
            }
        }

        var categoryDisplayName = GetCategoryDisplayName(request.Category);

        return new ExportMetadata(
            trainingTypeName,
            categoryDisplayName,
            technicalCategoryName,
            technicalCategoryDisplayName);
    }

    private static string BuildFileName(DateTimeOffset generatedAtUtc)
        => $"training-tracker-{generatedAtUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.xlsx";

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
        var lookup = categories
            .Where(category => category.IsActive)
            .ToLookup(category => category.ParentId);

        var options = new List<(int Id, string Text)>();

        void AddOptions(int? parentId, string prefix)
        {
            foreach (var category in lookup[parentId].OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                var text = string.IsNullOrEmpty(prefix)
                    ? category.Name
                    : string.Concat(prefix, category.Name);
                options.Add((category.Id, text));
                AddOptions(category.Id, string.Concat(prefix, "— "));
            }
        }

        AddOptions(null, string.Empty);

        var option = options.FirstOrDefault(item => item.Id == selectedId);
        if (option != default)
        {
            return option.Text;
        }

        var selected = categories.FirstOrDefault(category => category.Id == selectedId);
        return selected is null ? null : $"{selected.Name} (inactive)";
    }

    private sealed record ExportMetadata(
        string? TrainingTypeName,
        string? CategoryDisplayName,
        string? TechnicalCategoryName,
        string? TechnicalCategoryDisplayName);
}

public sealed record TrainingExportRequest(
    Guid? TrainingTypeId,
    TrainingCategory? Category,
    int? ProjectTechnicalCategoryId,
    DateOnly? From,
    DateOnly? To,
    string? Search,
    bool IncludeRoster,
    string RequestedByUserId);

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
