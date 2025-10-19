using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public interface IProliferationExportService
{
    Task<ProliferationExportResult> ExportAsync(ProliferationExportRequest request, CancellationToken cancellationToken);
}

public sealed class ProliferationExportService : IProliferationExportService
{
    private readonly ProliferationTrackerReadService _trackerReadService;
    private readonly IClock _clock;

    public ProliferationExportService(
        ProliferationTrackerReadService trackerReadService,
        IClock clock)
    {
        _trackerReadService = trackerReadService ?? throw new ArgumentNullException(nameof(trackerReadService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ProliferationExportResult> ExportAsync(ProliferationExportRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var validation = Validate(request);
        if (!validation.IsSuccessful)
        {
            return ProliferationExportResult.Failure(validation.Errors.ToArray());
        }

        var rows = new List<ProliferationExportRow>();
        foreach (var projectId in validation.ProjectIds)
        {
            for (var year = validation.YearFrom; year <= validation.YearTo; year++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var total = await _trackerReadService.GetEffectiveTotalAsync(
                    projectId,
                    validation.Source,
                    year,
                    cancellationToken);

                rows.Add(new ProliferationExportRow(projectId, validation.Source, year, total));
            }
        }

        var generatedAt = _clock.UtcNow;
        var file = BuildFile(rows, validation.Source, validation.YearFrom, validation.YearTo, generatedAt);
        return ProliferationExportResult.FromFile(file);
    }

    private static ProliferationExportFile BuildFile(
        IReadOnlyCollection<ProliferationExportRow> rows,
        ProliferationSource source,
        int yearFrom,
        int yearTo,
        DateTimeOffset generatedAtUtc)
    {
        var csv = BuildCsv(rows);
        var fileName = BuildFileName(source, yearFrom, yearTo, generatedAtUtc);
        return new ProliferationExportFile(fileName, csv, ProliferationExportFile.CsvContentType);
    }

    private static byte[] BuildCsv(IReadOnlyCollection<ProliferationExportRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ProjectId,Source,Year,TotalQuantity");

        foreach (var row in rows)
        {
            builder
                .Append(row.ProjectId.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(row.Source.ToString())
                .Append(',')
                .Append(row.Year.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(row.TotalQuantity.ToString(CultureInfo.InvariantCulture))
                .AppendLine();
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string BuildFileName(
        ProliferationSource source,
        int yearFrom,
        int yearTo,
        DateTimeOffset generatedAtUtc)
    {
        var sourceSegment = source.ToString();
        var yearSegment = yearFrom == yearTo ? yearFrom.ToString(CultureInfo.InvariantCulture) : $"{yearFrom}-{yearTo}";
        return $"Proliferation_{sourceSegment}_{yearSegment}_{generatedAtUtc:yyyyMMdd'T'HHmmss'Z'}.csv";
    }

    private static ValidationResult Validate(ProliferationExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
        {
            return ValidationResult.Fail("The requesting user could not be determined.");
        }

        if (request.ProjectIds is null || request.ProjectIds.Count == 0)
        {
            return ValidationResult.Fail("At least one project must be selected for export.");
        }

        if (request.YearFrom > request.YearTo)
        {
            return ValidationResult.Fail("The year range is invalid.");
        }

        if (!Enum.IsDefined(typeof(ProliferationSource), request.Source))
        {
            return ValidationResult.Fail("The proliferation source is invalid.");
        }

        return ValidationResult.Success(
            request.ProjectIds,
            request.Source,
            request.YearFrom,
            request.YearTo);
    }

    private sealed record ValidationResult(
        bool IsSuccessful,
        IReadOnlyCollection<string> Errors,
        IReadOnlyCollection<int> ProjectIds,
        ProliferationSource Source,
        int YearFrom,
        int YearTo)
    {
        public static ValidationResult Fail(params string[] errors) => new(false, errors, Array.Empty<int>(), default, 0, 0);

        public static ValidationResult Success(
            IReadOnlyCollection<int> projectIds,
            ProliferationSource source,
            int yearFrom,
            int yearTo)
            => new(true, Array.Empty<string>(), projectIds, source, yearFrom, yearTo);
    }
}

public sealed record ProliferationExportRequest(
    IReadOnlyCollection<int> ProjectIds,
    ProliferationSource Source,
    int YearFrom,
    int YearTo,
    string RequestedByUserId);

public sealed record ProliferationExportRow(
    int ProjectId,
    ProliferationSource Source,
    int Year,
    int TotalQuantity);

public sealed record ProliferationExportFile(string FileName, byte[] Content, string ContentType)
{
    public const string CsvContentType = "text/csv";
}

public sealed record ProliferationExportResult(bool Success, ProliferationExportFile? File, IReadOnlyList<string> Errors)
{
    public static ProliferationExportResult FromFile(ProliferationExportFile file)
        => new(true, file, Array.Empty<string>());

    public static ProliferationExportResult Failure(params string[] errors)
        => new(false, null, errors.Length == 0 ? new[] { "Export failed." } : errors);
}
