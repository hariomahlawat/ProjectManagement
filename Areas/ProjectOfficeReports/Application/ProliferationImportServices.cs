using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public interface IProliferationYearlyImportService
{
    Task<ProliferationImportResult> ImportAsync(ProliferationYearlyImportRequest request, CancellationToken cancellationToken);
}

public interface IProliferationGranularImportService
{
    Task<ProliferationImportResult> ImportAsync(ProliferationGranularImportRequest request, CancellationToken cancellationToken);
}

public sealed record ProliferationYearlyImportRequest(
    Stream Content,
    string? FileName,
    ProliferationSource Source,
    string UploadedByUserId);

public sealed record ProliferationGranularImportRequest(
    Stream Content,
    string? FileName,
    ProliferationSource Source,
    string UploadedByUserId);

public sealed record ProliferationImportResult(
    int ProcessedRows,
    int ImportedRows,
    IReadOnlyList<ProliferationImportRowError> Errors,
    ProliferationImportFile? RejectionFile)
{
    public bool HasErrors => Errors.Count > 0;
}

public sealed record ProliferationImportRowError(int RowNumber, string Message);

public sealed record ProliferationImportFile(string FileName, byte[] Content, string ContentType)
{
    public const string CsvContentType = "text/csv";
}

public sealed class ProliferationYearlyImportService : IProliferationYearlyImportService
{
    private static readonly string[] RequiredColumns =
    {
        "ProjectId",
        "Year",
        "DirectBeneficiaries",
        "IndirectBeneficiaries",
        "InvestmentValue"
    };

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<ProliferationYearlyImportService> _logger;

    public ProliferationYearlyImportService(
        ApplicationDbContext db,
        IClock clock,
        IAuditService audit,
        ILogger<ProliferationYearlyImportService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProliferationImportResult> ImportAsync(ProliferationYearlyImportRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.UploadedByUserId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(request.UploadedByUserId));
        }

        if (request.Source != ProliferationSource.Internal && request.Source != ProliferationSource.External)
        {
            return new ProliferationImportResult(0, 0, new[]
            {
                new ProliferationImportRowError(0, "Yearly imports are only supported for internal (SDD) or external (515) sources.")
            }, null);
        }

        if (request.Content is null)
        {
            throw new ArgumentNullException(nameof(request.Content));
        }

        var rows = new List<ParsedYearlyRow>();
        var errors = new List<ProliferationImportRowError>();
        var rejectedRows = new List<RejectedRow>();
        var auditChanges = new List<RecordChange>();

        using (var reader = new StreamReader(request.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
        using (var parser = new TextFieldParser(reader))
        {
            parser.SetDelimiters(",");
            parser.HasFieldsEnclosedInQuotes = true;

            if (parser.EndOfData)
            {
                return new ProliferationImportResult(0, 0, new[]
                {
                    new ProliferationImportRowError(0, "The uploaded file did not contain any data.")
                }, null);
            }

            var headers = parser.ReadFields();
            if (headers is null)
            {
                return new ProliferationImportResult(0, 0, new[]
                {
                    new ProliferationImportRowError(0, "The uploaded file header could not be read.")
                }, null);
            }

            var headerMap = BuildHeaderMap(headers);
            var missingColumns = RequiredColumns.Where(column => !headerMap.ContainsKey(column)).ToList();
            if (missingColumns.Count > 0)
            {
                return new ProliferationImportResult(0, 0, new[]
                {
                    new ProliferationImportRowError(0, $"Missing required columns: {string.Join(", ", missingColumns)}.")
                }, null);
            }

            var rowNumber = 1;
            while (!parser.EndOfData)
            {
                rowNumber++;
                string[]? fields = null;

                try
                {
                    fields = parser.ReadFields();
                }
                catch (MalformedLineException ex)
                {
                    var message = $"Row {rowNumber} could not be parsed: {ex.Message}";
                    errors.Add(new ProliferationImportRowError(rowNumber, message));
                    rejectedRows.Add(new RejectedRow(rowNumber, headers, fields ?? Array.Empty<string>(), message));
                    continue;
                }

                if (fields is null || fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                var raw = new string[headers.Length];
                Array.Copy(fields, raw, Math.Min(fields.Length, headers.Length));

                var parseResult = ParseYearlyRow(headerMap, fields, request.Source, rowNumber);
                if (!parseResult.Success)
                {
                    errors.Add(new ProliferationImportRowError(rowNumber, parseResult.Error!));
                    rejectedRows.Add(new RejectedRow(rowNumber, headers, raw, parseResult.Error!));
                    continue;
                }

                rows.Add(parseResult.Row!);
            }
        }

        if (rows.Count == 0)
        {
            var rejectionFile = rejectedRows.Count > 0
                ? BuildRejectionFile(request.FileName, rejectedRows)
                : null;

            return new ProliferationImportResult(0, 0, errors, rejectionFile);
        }

        var projectIds = rows.Select(r => r.ProjectId).Distinct().ToList();
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new { p.Id, p.LifecycleStatus })
            .ToListAsync(cancellationToken);
        var projectMap = projects.ToDictionary(p => p.Id);

        var invalidProjectRows = new List<ParsedYearlyRow>();
        foreach (var row in rows)
        {
            if (!projectMap.TryGetValue(row.ProjectId, out var project))
            {
                var message = $"Project {row.ProjectId} was not found.";
                errors.Add(new ProliferationImportRowError(row.RowNumber, message));
                rejectedRows.Add(new RejectedRow(row.RowNumber, row.Headers, row.RawValues, message));
                invalidProjectRows.Add(row);
                continue;
            }

            if (project.LifecycleStatus != ProjectLifecycleStatus.Completed)
            {
                var message = $"Project {row.ProjectId} is not marked as completed.";
                errors.Add(new ProliferationImportRowError(row.RowNumber, message));
                rejectedRows.Add(new RejectedRow(row.RowNumber, row.Headers, row.RawValues, message));
                invalidProjectRows.Add(row);
            }
        }

        if (invalidProjectRows.Count > 0)
        {
            foreach (var invalid in invalidProjectRows)
            {
                rows.Remove(invalid);
            }
        }

        if (rows.Count == 0)
        {
            var rejectionFile = rejectedRows.Count > 0
                ? BuildRejectionFile(request.FileName, rejectedRows)
                : null;

            return new ProliferationImportResult(0, 0, errors, rejectionFile);
        }

        var now = _clock.UtcNow;
        var years = rows.Select(r => r.Year).Distinct().ToList();

        var existing = await _db.ProliferationYearlies
            .Where(y => y.Source == request.Source && projectIds.Contains(y.ProjectId) && years.Contains(y.Year))
            .ToListAsync(cancellationToken);
        var existingMap = existing.ToDictionary(y => (y.ProjectId, y.Year));

        var imported = 0;
        foreach (var row in rows)
        {
            if (existingMap.TryGetValue((row.ProjectId, row.Year), out var entity))
            {
                ApplyMetrics(entity.Metrics, row.DirectBeneficiaries, row.IndirectBeneficiaries, row.InvestmentValue);
                entity.Notes = row.Notes;
                entity.LastModifiedByUserId = request.UploadedByUserId;
                entity.LastModifiedAtUtc = now;
                entity.RowVersion = Guid.NewGuid().ToByteArray();
                auditChanges.Add(RecordChange.Edited(row.ProjectId, request.Source, row.Year, row.DirectBeneficiaries, row.IndirectBeneficiaries, row.InvestmentValue));
            }
            else
            {
                var yearly = new ProliferationYearly
                {
                    Id = Guid.NewGuid(),
                    ProjectId = row.ProjectId,
                    Source = request.Source,
                    Year = row.Year,
                    Metrics = new ProliferationMetrics(),
                    Notes = row.Notes,
                    CreatedByUserId = request.UploadedByUserId,
                    CreatedAtUtc = now,
                    RowVersion = Guid.NewGuid().ToByteArray()
                };

                ApplyMetrics(yearly.Metrics, row.DirectBeneficiaries, row.IndirectBeneficiaries, row.InvestmentValue);
                _db.ProliferationYearlies.Add(yearly);
                auditChanges.Add(RecordChange.Created(row.ProjectId, request.Source, row.Year, row.DirectBeneficiaries, row.IndirectBeneficiaries, row.InvestmentValue));
            }

            imported++;
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (auditChanges.Count > 0)
        {
            foreach (var change in auditChanges)
            {
                var auditEvent = change.IsCreation
                    ? Audit.Events.ProliferationRecordCreated(
                        change.ProjectId,
                        change.Source,
                        change.Year,
                        change.DirectBeneficiaries,
                        change.IndirectBeneficiaries,
                        change.InvestmentValue,
                        request.UploadedByUserId,
                        origin: "Import")
                    : Audit.Events.ProliferationRecordEdited(
                        change.ProjectId,
                        change.Source,
                        change.Year,
                        change.DirectBeneficiaries,
                        change.IndirectBeneficiaries,
                        change.InvestmentValue,
                        request.UploadedByUserId,
                        origin: "Import");

                await auditEvent.WriteAsync(_audit);
            }

            await Audit.Events.ProliferationImportCompleted(
                    request.UploadedByUserId,
                    "Yearly",
                    request.Source,
                    request.FileName,
                    processedRows: rows.Count + rejectedRows.Count,
                    importedRows: imported,
                    errorCount: errors.Count)
                .WriteAsync(_audit);
        }

        var rejection = rejectedRows.Count > 0
            ? BuildRejectionFile(request.FileName, rejectedRows)
            : null;

        _logger.LogInformation(
            "Imported {Imported} yearly proliferation rows out of {Processed} from {FileName} for source {Source}.",
            imported,
            rows.Count + rejectedRows.Count,
            request.FileName ?? "(upload)",
            request.Source);

        return new ProliferationImportResult(rows.Count + rejectedRows.Count, imported, errors, rejection);
    }

    private static HeaderMap BuildHeaderMap(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Count; index++)
        {
            var header = headers[index]?.Trim();
            if (string.IsNullOrEmpty(header))
            {
                continue;
            }

            if (!map.ContainsKey(header))
            {
                map[header] = index;
            }
        }

        return new HeaderMap(headers.ToArray(), map);
    }

    private static ParseResult ParseYearlyRow(HeaderMap headerMap, IReadOnlyList<string> fields, ProliferationSource source, int rowNumber)
    {
        if (!TryGetInt(headerMap, fields, "ProjectId", out var projectId, out var error))
        {
            return ParseResult.Fail(error ?? "ProjectId is required.");
        }

        if (!TryGetInt(headerMap, fields, "Year", out var year, out error))
        {
            return ParseResult.Fail(error ?? "Year is required.");
        }

        int? direct = TryGetNullableInt(headerMap, fields, "DirectBeneficiaries", out error);
        if (error is not null)
        {
            return ParseResult.Fail(error);
        }

        int? indirect = TryGetNullableInt(headerMap, fields, "IndirectBeneficiaries", out error);
        if (error is not null)
        {
            return ParseResult.Fail(error);
        }

        decimal? investment = TryGetNullableDecimal(headerMap, fields, "InvestmentValue", out error);
        if (error is not null)
        {
            return ParseResult.Fail(error);
        }

        var notes = TryGetString(headerMap, fields, "Notes");

        return ParseResult.Success(new ParsedYearlyRow(
            rowNumber,
            projectId!.Value,
            year!.Value,
            direct,
            indirect,
            investment,
            notes,
            source,
            headerMap.Headers,
            CopyFields(headerMap.Headers, fields)));
    }

    private static void ApplyMetrics(ProliferationMetrics metrics, int? direct, int? indirect, decimal? investment)
    {
        metrics.DirectBeneficiaries = direct;
        metrics.IndirectBeneficiaries = indirect;
        metrics.InvestmentValue = investment;
    }

    private static ProliferationImportFile? BuildRejectionFile(string? fileName, IReadOnlyList<RejectedRow> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var headers = rows[0].Headers;
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', headers.Select(EscapeCsv)) + ",Error");

        foreach (var row in rows)
        {
            var lineValues = row.RawValues.Select(EscapeCsv).ToList();
            while (lineValues.Count < headers.Length)
            {
                lineValues.Add(string.Empty);
            }

            lineValues.Add(EscapeCsv(row.Error));
            builder.AppendLine(string.Join(',', lineValues));
        }

        var rejectionName = BuildRejectionFileName(fileName, "yearly");
        return new ProliferationImportFile(rejectionName, Encoding.UTF8.GetBytes(builder.ToString()), ProliferationImportFile.CsvContentType);
    }

    private static string BuildRejectionFileName(string? originalFileName, string suffix)
    {
        var baseName = string.IsNullOrWhiteSpace(originalFileName)
            ? "proliferation"
            : Path.GetFileNameWithoutExtension(originalFileName);

        return $"{baseName}-{suffix}-rejections-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.csv";
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsEscape = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsEscape)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string[] CopyFields(IReadOnlyList<string> headers, IReadOnlyList<string> fields)
    {
        var values = new string[headers.Count];
        for (var index = 0; index < headers.Count; index++)
        {
            values[index] = index < fields.Count ? fields[index] ?? string.Empty : string.Empty;
        }

        return values;
    }

    private static bool TryGetInt(HeaderMap map, IReadOnlyList<string> fields, string column, out int? value, out string? error)
    {
        value = null;
        error = null;

        if (!map.TryGetIndex(column, out var index))
        {
            error = $"Column '{column}' is missing.";
            return false;
        }

        if (index >= fields.Count || string.IsNullOrWhiteSpace(fields[index]))
        {
            error = $"Column '{column}' is required.";
            return false;
        }

        if (!int.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            error = $"Column '{column}' must be a whole number.";
            return false;
        }

        value = parsed;
        return true;
    }

    private static int? TryGetNullableInt(HeaderMap map, IReadOnlyList<string> fields, string column, out string? error)
    {
        error = null;
        if (!map.TryGetIndex(column, out var index) || index >= fields.Count)
        {
            return null;
        }

        var value = fields[index];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            error = $"Column '{column}' must be a whole number.";
            return null;
        }

        return parsed;
    }

    private static decimal? TryGetNullableDecimal(HeaderMap map, IReadOnlyList<string> fields, string column, out string? error)
    {
        error = null;
        if (!map.TryGetIndex(column, out var index) || index >= fields.Count)
        {
            return null;
        }

        var value = fields[index];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            error = $"Column '{column}' must be a number.";
            return null;
        }

        return parsed;
    }

    private static string? TryGetString(HeaderMap map, IReadOnlyList<string> fields, string column)
    {
        if (!map.TryGetIndex(column, out var index) || index >= fields.Count)
        {
            return null;
        }

        var value = fields[index];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record ParsedYearlyRow(
        int RowNumber,
        int ProjectId,
        int Year,
        int? DirectBeneficiaries,
        int? IndirectBeneficiaries,
        decimal? InvestmentValue,
        string? Notes,
        ProliferationSource Source,
        string[] Headers,
        string[] RawValues);

    private sealed record RecordChange(
        bool IsCreation,
        int ProjectId,
        ProliferationSource Source,
        int Year,
        int? DirectBeneficiaries,
        int? IndirectBeneficiaries,
        decimal? InvestmentValue)
    {
        public static RecordChange Created(
            int projectId,
            ProliferationSource source,
            int year,
            int? direct,
            int? indirect,
            decimal? investment) => new(true, projectId, source, year, direct, indirect, investment);

        public static RecordChange Edited(
            int projectId,
            ProliferationSource source,
            int year,
            int? direct,
            int? indirect,
            decimal? investment) => new(false, projectId, source, year, direct, indirect, investment);
    }

    private sealed record ParseResult(bool Success, ParsedYearlyRow? Row, string? Error)
    {
        public static ParseResult Success(ParsedYearlyRow row) => new(true, row, null);

        public static ParseResult Fail(string error) => new(false, null, error);
    }

    private sealed record HeaderMap(string[] Headers, Dictionary<string, int> Map)
    {
        public bool TryGetIndex(string column, out int index) => Map.TryGetValue(column, out index);
    }

    private sealed record RejectedRow(int RowNumber, string[] Headers, string[] RawValues, string Error);
}

public sealed class ProliferationGranularImportService : IProliferationGranularImportService
{
    private static readonly string[] RequiredColumns =
    {
        "ProjectId",
        "Year",
        "Granularity",
        "Period"
    };

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<ProliferationGranularImportService> _logger;

    public ProliferationGranularImportService(
        ApplicationDbContext db,
        IClock clock,
        IAuditService audit,
        ILogger<ProliferationGranularImportService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProliferationImportResult> ImportAsync(ProliferationGranularImportRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.UploadedByUserId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(request.UploadedByUserId));
        }

        if (request.Source != ProliferationSource.Internal)
        {
            return new ProliferationImportResult(0, 0, new[]
            {
                new ProliferationImportRowError(0, "Granular imports are only supported for the SDD source.")
            }, null);
        }

        if (request.Content is null)
        {
            throw new ArgumentNullException(nameof(request.Content));
        }

        var rows = new List<ParsedGranularRow>();
        var errors = new List<ProliferationImportRowError>();
        var rejectedRows = new List<RejectedRow>();
        var auditChanges = new List<GranularChange>();

        using (var reader = new StreamReader(request.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
        using (var parser = new TextFieldParser(reader))
        {
            parser.SetDelimiters(",");
            parser.HasFieldsEnclosedInQuotes = true;

            if (parser.EndOfData)
            {
                return new ProliferationImportResult(0, 0, new[]
                {
                    new ProliferationImportRowError(0, "The uploaded file did not contain any data.")
                }, null);
            }

            var headers = parser.ReadFields();
            if (headers is null)
            {
                return new ProliferationImportResult(0, 0, new[]
                {
                    new ProliferationImportRowError(0, "The uploaded file header could not be read.")
                }, null);
            }

            var headerMap = BuildHeaderMap(headers);
            var missingColumns = RequiredColumns.Where(column => !headerMap.ContainsKey(column)).ToList();
            if (missingColumns.Count > 0)
            {
                return new ProliferationImportResult(0, 0, new[]
                {
                    new ProliferationImportRowError(0, $"Missing required columns: {string.Join(", ", missingColumns)}.")
                }, null);
            }

            var rowNumber = 1;
            while (!parser.EndOfData)
            {
                rowNumber++;
                string[]? fields = null;

                try
                {
                    fields = parser.ReadFields();
                }
                catch (MalformedLineException ex)
                {
                    var message = $"Row {rowNumber} could not be parsed: {ex.Message}";
                    errors.Add(new ProliferationImportRowError(rowNumber, message));
                    rejectedRows.Add(new RejectedRow(rowNumber, headers, fields ?? Array.Empty<string>(), message));
                    continue;
                }

                if (fields is null || fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                var raw = new string[headers.Length];
                Array.Copy(fields, raw, Math.Min(fields.Length, headers.Length));

                var parseResult = ParseGranularRow(headerMap, fields, request.Source, rowNumber);
                if (!parseResult.Success)
                {
                    errors.Add(new ProliferationImportRowError(rowNumber, parseResult.Error!));
                    rejectedRows.Add(new RejectedRow(rowNumber, headers, raw, parseResult.Error!));
                    continue;
                }

                rows.Add(parseResult.Row!);
            }
        }

        if (rows.Count == 0)
        {
            var rejectionFile = rejectedRows.Count > 0
                ? BuildRejectionFile(request.FileName, rejectedRows)
                : null;

            return new ProliferationImportResult(0, 0, errors, rejectionFile);
        }

        var projectIds = rows.Select(r => r.ProjectId).Distinct().ToList();
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new { p.Id, p.LifecycleStatus })
            .ToListAsync(cancellationToken);
        var projectMap = projects.ToDictionary(p => p.Id);

        var invalidRows = new List<ParsedGranularRow>();
        foreach (var row in rows)
        {
            if (!projectMap.TryGetValue(row.ProjectId, out var project))
            {
                var message = $"Project {row.ProjectId} was not found.";
                errors.Add(new ProliferationImportRowError(row.RowNumber, message));
                rejectedRows.Add(new RejectedRow(row.RowNumber, row.Headers, row.RawValues, message));
                invalidRows.Add(row);
                continue;
            }

            if (project.LifecycleStatus != ProjectLifecycleStatus.Completed)
            {
                var message = $"Project {row.ProjectId} is not marked as completed.";
                errors.Add(new ProliferationImportRowError(row.RowNumber, message));
                rejectedRows.Add(new RejectedRow(row.RowNumber, row.Headers, row.RawValues, message));
                invalidRows.Add(row);
            }
        }

        if (invalidRows.Count > 0)
        {
            foreach (var invalid in invalidRows)
            {
                rows.Remove(invalid);
            }
        }

        if (rows.Count == 0)
        {
            var rejectionFile = rejectedRows.Count > 0
                ? BuildRejectionFile(request.FileName, rejectedRows)
                : null;

            return new ProliferationImportResult(0, 0, errors, rejectionFile);
        }

        var now = _clock.UtcNow;
        var years = rows.Select(r => r.Year).Distinct().ToList();

        var existing = await _db.ProliferationGranularEntries
            .Where(g => g.Source == request.Source && projectIds.Contains(g.ProjectId) && years.Contains(g.Year))
            .ToListAsync(cancellationToken);

        var existingMap = existing.ToDictionary(g => (g.ProjectId, g.Year, g.Granularity, g.Period));
        var imported = 0;

        foreach (var row in rows)
        {
            if (existingMap.TryGetValue((row.ProjectId, row.Year, row.Granularity, row.Period), out var entity))
            {
                entity.PeriodLabel = row.PeriodLabel;
                ApplyMetrics(entity.Metrics, row.DirectBeneficiaries, row.IndirectBeneficiaries, row.InvestmentValue);
                entity.Notes = row.Notes;
                entity.LastModifiedByUserId = request.UploadedByUserId;
                entity.LastModifiedAtUtc = now;
                entity.RowVersion = Guid.NewGuid().ToByteArray();
                auditChanges.Add(GranularChange.Edited(row.ProjectId, request.Source, row.Year, row.Granularity, row.Period, row.PeriodLabel, row.DirectBeneficiaries, row.IndirectBeneficiaries, row.InvestmentValue));
            }
            else
            {
                var granular = new ProliferationGranular
                {
                    Id = Guid.NewGuid(),
                    ProjectId = row.ProjectId,
                    Source = request.Source,
                    Year = row.Year,
                    Granularity = row.Granularity,
                    Period = row.Period,
                    PeriodLabel = row.PeriodLabel,
                    Metrics = new ProliferationMetrics(),
                    Notes = row.Notes,
                    CreatedByUserId = request.UploadedByUserId,
                    CreatedAtUtc = now,
                    RowVersion = Guid.NewGuid().ToByteArray()
                };

                ApplyMetrics(granular.Metrics, row.DirectBeneficiaries, row.IndirectBeneficiaries, row.InvestmentValue);
                _db.ProliferationGranularEntries.Add(granular);
                auditChanges.Add(GranularChange.Created(row.ProjectId, request.Source, row.Year, row.Granularity, row.Period, row.PeriodLabel, row.DirectBeneficiaries, row.IndirectBeneficiaries, row.InvestmentValue));
            }

            imported++;
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (auditChanges.Count > 0)
        {
            foreach (var change in auditChanges)
            {
                var auditEvent = change.IsCreation
                    ? Audit.Events.ProliferationRecordCreated(
                        change.ProjectId,
                        change.Source,
                        change.Year,
                        change.DirectBeneficiaries,
                        change.IndirectBeneficiaries,
                        change.InvestmentValue,
                        request.UploadedByUserId,
                        origin: "Import",
                        granularity: change.Granularity,
                        period: change.Period,
                        periodLabel: change.PeriodLabel)
                    : Audit.Events.ProliferationRecordEdited(
                        change.ProjectId,
                        change.Source,
                        change.Year,
                        change.DirectBeneficiaries,
                        change.IndirectBeneficiaries,
                        change.InvestmentValue,
                        request.UploadedByUserId,
                        origin: "Import",
                        granularity: change.Granularity,
                        period: change.Period,
                        periodLabel: change.PeriodLabel);

                await auditEvent.WriteAsync(_audit);
            }

            await Audit.Events.ProliferationImportCompleted(
                    request.UploadedByUserId,
                    "Granular",
                    request.Source,
                    request.FileName,
                    processedRows: rows.Count + rejectedRows.Count,
                    importedRows: imported,
                    errorCount: errors.Count)
                .WriteAsync(_audit);
        }

        var rejection = rejectedRows.Count > 0
            ? BuildRejectionFile(request.FileName, rejectedRows)
            : null;

        _logger.LogInformation(
            "Imported {Imported} granular proliferation rows out of {Processed} from {FileName}.",
            imported,
            rows.Count + rejectedRows.Count,
            request.FileName ?? "(upload)");

        return new ProliferationImportResult(rows.Count + rejectedRows.Count, imported, errors, rejection);
    }

    private static HeaderMap BuildHeaderMap(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Count; index++)
        {
            var header = headers[index]?.Trim();
            if (string.IsNullOrEmpty(header))
            {
                continue;
            }

            if (!map.ContainsKey(header))
            {
                map[header] = index;
            }
        }

        return new HeaderMap(headers.ToArray(), map);
    }

    private static ParseResult ParseGranularRow(HeaderMap headerMap, IReadOnlyList<string> fields, ProliferationSource source, int rowNumber)
    {
        if (!TryGetInt(headerMap, fields, "ProjectId", out var projectId, out var error))
        {
            return ParseResult.Fail(error ?? "ProjectId is required.");
        }

        if (!TryGetInt(headerMap, fields, "Year", out var year, out error))
        {
            return ParseResult.Fail(error ?? "Year is required.");
        }

        var granularityValue = TryGetString(headerMap, fields, "Granularity");
        if (string.IsNullOrWhiteSpace(granularityValue))
        {
            return ParseResult.Fail("Granularity is required.");
        }

        if (!Enum.TryParse<ProliferationGranularity>(granularityValue, ignoreCase: true, out var granularity))
        {
            return ParseResult.Fail($"Granularity '{granularityValue}' is not supported. Use Monthly or Quarterly.");
        }

        if (!TryGetInt(headerMap, fields, "Period", out var period, out error))
        {
            return ParseResult.Fail(error ?? "Period is required.");
        }

        if (!IsValidPeriod(granularity, period!.Value))
        {
            return ParseResult.Fail($"Period '{period}' is not valid for {granularity} granularity.");
        }

        int? direct = TryGetNullableInt(headerMap, fields, "DirectBeneficiaries", out error);
        if (error is not null)
        {
            return ParseResult.Fail(error);
        }

        int? indirect = TryGetNullableInt(headerMap, fields, "IndirectBeneficiaries", out error);
        if (error is not null)
        {
            return ParseResult.Fail(error);
        }

        decimal? investment = TryGetNullableDecimal(headerMap, fields, "InvestmentValue", out error);
        if (error is not null)
        {
            return ParseResult.Fail(error);
        }

        var periodLabel = TryGetString(headerMap, fields, "PeriodLabel");
        var notes = TryGetString(headerMap, fields, "Notes");

        return ParseResult.Success(new ParsedGranularRow(
            rowNumber,
            projectId!.Value,
            year!.Value,
            granularity,
            period!.Value,
            periodLabel,
            direct,
            indirect,
            investment,
            notes,
            source,
            headerMap.Headers,
            CopyFields(headerMap.Headers, fields)));
    }

    private static bool IsValidPeriod(ProliferationGranularity granularity, int period)
    {
        return granularity switch
        {
            ProliferationGranularity.Monthly => period >= 1 && period <= 12,
            ProliferationGranularity.Quarterly => period >= 1 && period <= 4,
            _ => false
        };
    }

    private static void ApplyMetrics(ProliferationMetrics metrics, int? direct, int? indirect, decimal? investment)
    {
        metrics.DirectBeneficiaries = direct;
        metrics.IndirectBeneficiaries = indirect;
        metrics.InvestmentValue = investment;
    }

    private static ProliferationImportFile? BuildRejectionFile(string? fileName, IReadOnlyList<RejectedRow> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var headers = rows[0].Headers;
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', headers.Select(EscapeCsv)) + ",Error");

        foreach (var row in rows)
        {
            var lineValues = row.RawValues.Select(EscapeCsv).ToList();
            while (lineValues.Count < headers.Length)
            {
                lineValues.Add(string.Empty);
            }

            lineValues.Add(EscapeCsv(row.Error));
            builder.AppendLine(string.Join(',', lineValues));
        }

        var rejectionName = BuildRejectionFileName(fileName, "granular");
        return new ProliferationImportFile(rejectionName, Encoding.UTF8.GetBytes(builder.ToString()), ProliferationImportFile.CsvContentType);
    }

    private static string BuildRejectionFileName(string? originalFileName, string suffix)
    {
        var baseName = string.IsNullOrWhiteSpace(originalFileName)
            ? "proliferation"
            : Path.GetFileNameWithoutExtension(originalFileName);

        return $"{baseName}-{suffix}-rejections-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.csv";
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsEscape = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsEscape)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string[] CopyFields(IReadOnlyList<string> headers, IReadOnlyList<string> fields)
    {
        var values = new string[headers.Count];
        for (var index = 0; index < headers.Count; index++)
        {
            values[index] = index < fields.Count ? fields[index] ?? string.Empty : string.Empty;
        }

        return values;
    }

    private static bool TryGetInt(HeaderMap map, IReadOnlyList<string> fields, string column, out int? value, out string? error)
    {
        value = null;
        error = null;

        if (!map.TryGetIndex(column, out var index))
        {
            error = $"Column '{column}' is missing.";
            return false;
        }

        if (index >= fields.Count || string.IsNullOrWhiteSpace(fields[index]))
        {
            error = $"Column '{column}' is required.";
            return false;
        }

        if (!int.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            error = $"Column '{column}' must be a whole number.";
            return false;
        }

        value = parsed;
        return true;
    }

    private static int? TryGetNullableInt(HeaderMap map, IReadOnlyList<string> fields, string column, out string? error)
    {
        error = null;
        if (!map.TryGetIndex(column, out var index) || index >= fields.Count)
        {
            return null;
        }

        var value = fields[index];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            error = $"Column '{column}' must be a whole number.";
            return null;
        }

        return parsed;
    }

    private static decimal? TryGetNullableDecimal(HeaderMap map, IReadOnlyList<string> fields, string column, out string? error)
    {
        error = null;
        if (!map.TryGetIndex(column, out var index) || index >= fields.Count)
        {
            return null;
        }

        var value = fields[index];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            error = $"Column '{column}' must be a number.";
            return null;
        }

        return parsed;
    }

    private static string? TryGetString(HeaderMap map, IReadOnlyList<string> fields, string column)
    {
        if (!map.TryGetIndex(column, out var index) || index >= fields.Count)
        {
            return null;
        }

        var value = fields[index];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record ParsedGranularRow(
        int RowNumber,
        int ProjectId,
        int Year,
        ProliferationGranularity Granularity,
        int Period,
        string? PeriodLabel,
        int? DirectBeneficiaries,
        int? IndirectBeneficiaries,
        decimal? InvestmentValue,
        string? Notes,
        ProliferationSource Source,
        string[] Headers,
        string[] RawValues);

    private sealed record GranularChange(
        bool IsCreation,
        int ProjectId,
        ProliferationSource Source,
        int Year,
        ProliferationGranularity Granularity,
        int Period,
        string? PeriodLabel,
        int? DirectBeneficiaries,
        int? IndirectBeneficiaries,
        decimal? InvestmentValue)
    {
        public static GranularChange Created(
            int projectId,
            ProliferationSource source,
            int year,
            ProliferationGranularity granularity,
            int period,
            string? periodLabel,
            int? direct,
            int? indirect,
            decimal? investment) => new(true, projectId, source, year, granularity, period, periodLabel, direct, indirect, investment);

        public static GranularChange Edited(
            int projectId,
            ProliferationSource source,
            int year,
            ProliferationGranularity granularity,
            int period,
            string? periodLabel,
            int? direct,
            int? indirect,
            decimal? investment) => new(false, projectId, source, year, granularity, period, periodLabel, direct, indirect, investment);
    }

    private sealed record ParseResult(bool Success, ParsedGranularRow? Row, string? Error)
    {
        public static ParseResult Success(ParsedGranularRow row) => new(true, row, null);

        public static ParseResult Fail(string error) => new(false, null, error);
    }

    private sealed record HeaderMap(string[] Headers, Dictionary<string, int> Map)
    {
        public bool TryGetIndex(string column, out int index) => Map.TryGetValue(column, out index);
    }

    private sealed record RejectedRow(int RowNumber, string[] Headers, string[] RawValues, string Error);
}
