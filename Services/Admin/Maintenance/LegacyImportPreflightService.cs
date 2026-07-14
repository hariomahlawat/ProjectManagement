using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.Admin.Maintenance;

public enum LegacyImportRowStatus
{
    Valid = 0,
    Warning = 1,
    Rejected = 2
}

public sealed record LegacyImportPreviewRow(
    int RowNumber,
    string? Nomenclature,
    string? ArmService,
    string? YearOfDevelopment,
    string? CostLakhs,
    LegacyImportRowStatus Status,
    string Message);

public sealed record LegacyImportPreview(
    Guid Token,
    string FileName,
    string FileHashSha256,
    int RowsReceived,
    int ValidRows,
    int WarningRows,
    int RejectedRows,
    bool DuplicateCategoryImport,
    DateTimeOffset ExpiresUtc,
    IReadOnlyList<LegacyImportPreviewRow> Rows)
{
    public bool CanCommit => RowsReceived > 0 && RejectedRows == 0 && !DuplicateCategoryImport;
}

public sealed record LegacyImportCommitResult(
    bool Succeeded,
    int RowsImported,
    string Message,
    string? TraceId = null);

public interface ILegacyImportPreflightService
{
    Task<AdminOperationResult<LegacyImportPreview>> PreviewAsync(
        int projectCategoryId,
        int technicalCategoryId,
        IFormFile file,
        CancellationToken cancellationToken = default);

    Task<LegacyImportCommitResult> CommitAsync(
        Guid token,
        int projectCategoryId,
        int technicalCategoryId,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task CancelAsync(Guid token, CancellationToken cancellationToken = default);
}

public sealed class LegacyImportPreflightService : ILegacyImportPreflightService
{
    private static readonly string[] RequiredHeaders = { "Nomenclature" };
    private const long MaximumFileBytes = 10L * 1024 * 1024;

    private readonly ApplicationDbContext _db;
    private readonly IProjectImportService _import;
    private readonly IAdminAuditService _audit;
    private readonly IWebHostEnvironment _environment;
    private readonly AdminRecoveryOptions _options;
    private readonly ILogger<LegacyImportPreflightService> _logger;

    public LegacyImportPreflightService(
        ApplicationDbContext db,
        IProjectImportService import,
        IAdminAuditService audit,
        IWebHostEnvironment environment,
        IOptions<AdminRecoveryOptions> options,
        ILogger<LegacyImportPreflightService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _import = import ?? throw new ArgumentNullException(nameof(import));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AdminOperationResult<LegacyImportPreview>> PreviewAsync(
        int projectCategoryId,
        int technicalCategoryId,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (projectCategoryId <= 0 || technicalCategoryId <= 0)
            return AdminOperationResult<LegacyImportPreview>.Failure(
                "Select both project and technical categories.",
                "LegacyImportCategoriesRequired");
        if (file is null || file.Length <= 0)
            return AdminOperationResult<LegacyImportPreview>.Failure(
                "Upload a non-empty Excel workbook.",
                "LegacyImportFileRequired");
        if (file.Length > MaximumFileBytes)
            return AdminOperationResult<LegacyImportPreview>.Failure(
                "The workbook exceeds the 10 MB administrative import limit.",
                "LegacyImportFileTooLarge");
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return AdminOperationResult<LegacyImportPreview>.Failure(
                "Upload an .xlsx workbook created from the approved template.",
                "LegacyImportInvalidFileType");

        // These queries share one scoped DbContext and therefore must not run in
        // parallel. Keeping them sequential also makes cancellation deterministic.
        var categoryExists = await _db.ProjectCategories.AsNoTracking()
            .AnyAsync(category => category.Id == projectCategoryId, cancellationToken);
        var technicalExists = await _db.TechnicalCategories.AsNoTracking()
            .AnyAsync(category => category.Id == technicalCategoryId && category.IsActive, cancellationToken);
        var duplicateImport = await _db.ProjectLegacyImports.AsNoTracking()
            .AnyAsync(row => row.ProjectCategoryId == projectCategoryId
                && row.TechnicalCategoryId == technicalCategoryId, cancellationToken);

        if (!categoryExists || !technicalExists)
            return AdminOperationResult<LegacyImportPreview>.Failure(
                "The selected categories are no longer available.",
                "LegacyImportCategoryUnavailable");

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        LegacyImportPreview preview;
        try
        {
            buffer.Position = 0;
            preview = await BuildPreviewAsync(
                projectCategoryId,
                technicalCategoryId,
                file.FileName,
                hash,
                buffer,
                duplicateImport,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Legacy import workbook validation failed.");
            return AdminOperationResult<LegacyImportPreview>.Failure(
                "The workbook could not be read. Download the approved template and verify the file.",
                "LegacyImportWorkbookInvalid");
        }

        if (preview.RowsReceived == 0)
            return AdminOperationResult<LegacyImportPreview>.Failure(
                "No populated data rows were found in the workbook.",
                "LegacyImportNoRows");

        await StageAsync(preview, projectCategoryId, technicalCategoryId, bytes, cancellationToken);
        return AdminOperationResult<LegacyImportPreview>.Success(
            preview,
            preview.CanCommit
                ? "Validation completed. Review the proposed import before committing."
                : "Validation completed with issues that must be resolved before import.");
    }

    public async Task<LegacyImportCommitResult> CommitAsync(
        Guid token,
        int projectCategoryId,
        int technicalCategoryId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var traceId = Guid.NewGuid().ToString("N");
        try
        {
            var staged = await LoadStagedAsync(token, cancellationToken);
            if (staged is null)
                return new(false, 0, "The validated import has expired or is no longer available.");
            if (staged.ProjectCategoryId != projectCategoryId
                || staged.TechnicalCategoryId != technicalCategoryId)
                return new(false, 0, "The selected categories do not match the validated import.");
            if (staged.ExpiresUtc <= DateTimeOffset.UtcNow)
            {
                await DeleteStagedAsync(token);
                return new(false, 0, "The validated import has expired. Validate the workbook again.");
            }
            if (staged.RejectedRows > 0 || staged.DuplicateCategoryImport)
                return new(false, 0, "The validated import contains blocking issues and cannot be committed.");

            var workbookPath = WorkbookPath(token);
            if (!File.Exists(workbookPath))
                return new(false, 0, "The staged workbook is no longer available.");

            var stagedBytes = await File.ReadAllBytesAsync(workbookPath, cancellationToken);
            var stagedHash = Convert.ToHexString(SHA256.HashData(stagedBytes));
            if (!string.Equals(stagedHash, staged.FileHashSha256, StringComparison.OrdinalIgnoreCase))
            {
                await DeleteStagedAsync(token);
                return new(false, 0, "The staged workbook failed its integrity check. Validate the source file again.");
            }

            await using var stream = new MemoryStream(stagedBytes, writable: false);
            var formFile = new FormFile(stream, 0, stream.Length, "Upload", staged.FileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            };
            var result = await _import.ImportLegacyProjectsAsync(
                projectCategoryId,
                technicalCategoryId,
                formFile,
                actorUserId);

            if (!result.Success)
                return new(false, 0, result.ErrorMessage ?? "The import could not be completed.", traceId);

            await _audit.RecordAsync(
                new AdminAuditEntry(
                    Action: "LegacyImportCommitted",
                    EntityType: "ProjectLegacyImport",
                    EntityId: token.ToString("N"),
                    After: new
                    {
                        ProjectCategoryId = projectCategoryId,
                        TechnicalCategoryId = technicalCategoryId,
                        result.RowsImported,
                        staged.FileHashSha256,
                        staged.FileName
                    },
                    Origin: "Admin.Maintenance.LegacyImport",
                    Message: $"Imported {result.RowsImported} legacy project(s)."),
                cancellationToken);

            await DeleteStagedAsync(token);
            return new(true, result.RowsImported, $"Imported {result.RowsImported} legacy project(s).", traceId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Legacy import commit failed. Token={Token}, TraceId={TraceId}", token, traceId);
            return new(false, 0, "The import could not be completed. Quote the trace reference to the administrator.", traceId);
        }
    }

    public async Task CancelAsync(Guid token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await DeleteStagedAsync(token);
    }

    private async Task<LegacyImportPreview> BuildPreviewAsync(
        int projectCategoryId,
        int technicalCategoryId,
        string fileName,
        string hash,
        Stream stream,
        bool duplicateCategoryImport,
        CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidDataException("Worksheet not found.");
        var headerCells = worksheet.Row(1).CellsUsed().ToList();
        var headers = headerCells
            .Select(cell => new { Name = cell.GetString().Trim(), Column = cell.Address.ColumnNumber })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Column, StringComparer.OrdinalIgnoreCase);
        if (!RequiredHeaders.All(headers.ContainsKey))
            throw new InvalidDataException("Required headers are missing.");

        var existingNames = await _db.Projects.IgnoreQueryFilters().AsNoTracking()
            .Select(project => project.Name)
            .ToListAsync(cancellationToken);
        var existing = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var workbookNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<LegacyImportPreviewRow>();
        var received = 0;
        var valid = 0;
        var warnings = 0;
        var rejected = 0;

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RowIsEmpty(row)) continue;
            received++;

            var rowNumber = row.RowNumber();
            var name = Get(row, headers, "Nomenclature");
            var arm = Get(row, headers, "ArmService");
            var year = Get(row, headers, "YearOfDevp");
            var cost = Get(row, headers, "CostLakhs");
            var status = LegacyImportRowStatus.Valid;
            var messages = new List<string>();

            if (string.IsNullOrWhiteSpace(name))
            {
                status = LegacyImportRowStatus.Rejected;
                messages.Add("Nomenclature is required.");
            }
            else
            {
                if (name.Length > 100)
                {
                    status = LegacyImportRowStatus.Warning;
                    messages.Add("Nomenclature will be truncated to 100 characters.");
                }
                if (!workbookNames.Add(name))
                {
                    status = LegacyImportRowStatus.Warning;
                    messages.Add("Duplicate nomenclature appears in this workbook.");
                }
                if (existing.Contains(name))
                {
                    status = LegacyImportRowStatus.Warning;
                    messages.Add("A project with the same nomenclature already exists.");
                }
            }

            if (!string.IsNullOrWhiteSpace(year)
                && (!short.TryParse(year, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedYear)
                    || parsedYear is < 1900 or > 2100))
            {
                status = status == LegacyImportRowStatus.Rejected ? status : LegacyImportRowStatus.Warning;
                messages.Add("YearOfDevp is invalid and will be left blank.");
            }
            if (!string.IsNullOrWhiteSpace(cost)
                && (!decimal.TryParse(cost, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedCost)
                    || parsedCost < 0))
            {
                status = status == LegacyImportRowStatus.Rejected ? status : LegacyImportRowStatus.Warning;
                messages.Add("CostLakhs is invalid and will be left blank.");
            }

            switch (status)
            {
                case LegacyImportRowStatus.Valid: valid++; break;
                case LegacyImportRowStatus.Warning: warnings++; break;
                case LegacyImportRowStatus.Rejected: rejected++; break;
            }

            if (rows.Count < _options.LegacyImportPreviewRows)
            {
                rows.Add(new LegacyImportPreviewRow(
                    rowNumber,
                    name,
                    arm,
                    year,
                    cost,
                    status,
                    messages.Count == 0 ? "Ready to import." : string.Join(" ", messages)));
            }
        }

        if (duplicateCategoryImport)
            rejected = Math.Max(1, rejected);

        return new LegacyImportPreview(
            Guid.NewGuid(),
            Path.GetFileName(fileName),
            hash,
            received,
            valid,
            warnings,
            rejected,
            duplicateCategoryImport,
            DateTimeOffset.UtcNow.AddMinutes(_options.LegacyImportStagingMinutes),
            rows);
    }

    private async Task StageAsync(
        LegacyImportPreview preview,
        int projectCategoryId,
        int technicalCategoryId,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(StagingRoot());
        await File.WriteAllBytesAsync(WorkbookPath(preview.Token), bytes, cancellationToken);
        var metadata = new StagedLegacyImport(
            preview.Token,
            preview.FileName,
            preview.FileHashSha256,
            projectCategoryId,
            technicalCategoryId,
            preview.RowsReceived,
            preview.ValidRows,
            preview.WarningRows,
            preview.RejectedRows,
            preview.DuplicateCategoryImport,
            preview.ExpiresUtc);
        await using var metadataStream = File.Create(MetadataPath(preview.Token));
        await JsonSerializer.SerializeAsync(metadataStream, metadata, cancellationToken: cancellationToken);
    }

    private async Task<StagedLegacyImport?> LoadStagedAsync(Guid token, CancellationToken cancellationToken)
    {
        var path = MetadataPath(token);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<StagedLegacyImport>(stream, cancellationToken: cancellationToken);
    }

    private Task DeleteStagedAsync(Guid token)
    {
        TryDelete(WorkbookPath(token));
        TryDelete(MetadataPath(token));
        return Task.CompletedTask;
    }

    private string StagingRoot() => Path.Combine(_environment.ContentRootPath, "App_Data", "AdminImportStaging");
    private string WorkbookPath(Guid token) => Path.Combine(StagingRoot(), $"{token:N}.xlsx");
    private string MetadataPath(Guid token) => Path.Combine(StagingRoot(), $"{token:N}.json");

    private static string? Get(IXLRow row, IReadOnlyDictionary<string, int> headers, string name) =>
        headers.TryGetValue(name, out var column) ? row.Cell(column).GetString().Trim() : null;

    private static bool RowIsEmpty(IXLRow row) =>
        row.CellsUsed().All(cell => string.IsNullOrWhiteSpace(cell.GetString()));

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* Expired staging files are non-authoritative and may be cleaned later. */ }
    }

    private sealed record StagedLegacyImport(
        Guid Token,
        string FileName,
        string FileHashSha256,
        int ProjectCategoryId,
        int TechnicalCategoryId,
        int RowsReceived,
        int ValidRows,
        int WarningRows,
        int RejectedRows,
        bool DuplicateCategoryImport,
        DateTimeOffset ExpiresUtc);
}
