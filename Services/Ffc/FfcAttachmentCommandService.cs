using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Application.Ffc;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Services.Ffc;

public sealed record FfcAttachmentUploadResult(
    bool Success,
    long? AttachmentId = null,
    string? Message = null,
    string? Warning = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);

public interface IFfcAttachmentCommandService
{
    Task<FfcAttachmentUploadResult> UploadAsync(
        long recordId,
        IFormFile? file,
        string? caption,
        CancellationToken cancellationToken = default);

    Task<FfcCommandResult> DeleteAsync(
        long recordId,
        long attachmentId,
        CancellationToken cancellationToken = default);
}

public sealed class FfcAttachmentCommandService : IFfcAttachmentCommandService
{
    private readonly ApplicationDbContext _db;
    private readonly IFfcAttachmentStorage _storage;
    private readonly IDocRepoIngestionService _docRepoIngestionService;
    private readonly IUploadPathResolver _pathResolver;
    private readonly IAuditService _audit;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<FfcAttachmentCommandService> _logger;

    public FfcAttachmentCommandService(
        ApplicationDbContext db,
        IFfcAttachmentStorage storage,
        IDocRepoIngestionService docRepoIngestionService,
        IUploadPathResolver pathResolver,
        IAuditService audit,
        IHttpContextAccessor httpContextAccessor,
        ILogger<FfcAttachmentCommandService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _docRepoIngestionService = docRepoIngestionService ?? throw new ArgumentNullException(nameof(docRepoIngestionService));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FfcAttachmentUploadResult> UploadAsync(
        long recordId,
        IFormFile? file,
        string? caption,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return new FfcAttachmentUploadResult(
                false,
                FieldErrors: Error("UploadFile", "Select a PDF or image to upload."));
        }

        var recordExists = await _db.FfcRecords
            .AsNoTracking()
            .AnyAsync(record => record.Id == recordId && !record.IsDeleted, cancellationToken);

        if (!recordExists)
        {
            return new FfcAttachmentUploadResult(false, Message: "The FFC record was not found or has been archived.");
        }

        var kind = ResolveKind(file.ContentType);
        if (!kind.HasValue)
        {
            return new FfcAttachmentUploadResult(
                false,
                FieldErrors: Error("UploadFile", "Only PDF, JPEG, PNG and WEBP files are supported."));
        }

        var normalizedCaption = string.IsNullOrWhiteSpace(caption)
            ? Path.GetFileNameWithoutExtension(file.FileName)
            : caption.Trim();

        (bool Success, string? ErrorMessage, FfcAttachment? Attachment) storageResult;
        try
        {
            storageResult = await _storage.SaveAsync(
                recordId,
                file,
                kind.Value,
                normalizedCaption);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to store FFC attachment for record {RecordId}.",
                recordId);
            return new FfcAttachmentUploadResult(
                false,
                Message: "The file could not be stored. No attachment was added.",
                FieldErrors: Error("UploadFile", "The file could not be stored. Try again or contact the administrator."));
        }

        if (!storageResult.Success || storageResult.Attachment is null)
        {
            return new FfcAttachmentUploadResult(
                false,
                Message: storageResult.ErrorMessage ?? "Unable to upload the file.",
                FieldErrors: Error("UploadFile", storageResult.ErrorMessage ?? "Unable to upload the file."));
        }

        var attachment = storageResult.Attachment;
        string? warning = null;

        if (string.Equals(attachment.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var absolutePath = _pathResolver.ToAbsolute(attachment.FilePath);
                await using var stream = File.OpenRead(absolutePath);
                await _docRepoIngestionService.IngestExternalPdfAsync(
                    stream,
                    file.FileName,
                    "FFC",
                    attachment.Id.ToString(CultureInfo.InvariantCulture),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                warning = "The file was uploaded, but document indexing could not be completed. It remains available in this workspace.";
                _logger.LogError(
                    exception,
                    "Failed to ingest FFC attachment {AttachmentId} into the document repository.",
                    attachment.Id);
            }
        }

        await TryAuditAsync(
            "ProjectOfficeReports.FFC.AttachmentUploaded",
            attachment,
            file.FileName);

        return new FfcAttachmentUploadResult(
            true,
            attachment.Id,
            "Attachment uploaded.",
            warning);
    }

    public async Task<FfcCommandResult> DeleteAsync(
        long recordId,
        long attachmentId,
        CancellationToken cancellationToken = default)
    {
        var attachment = await _db.FfcAttachments
            .FirstOrDefaultAsync(item => item.Id == attachmentId && item.FfcRecordId == recordId, cancellationToken);

        if (attachment is null)
        {
            return FfcCommandResult.Invalid("The attachment was not found.");
        }

        var snapshot = new Dictionary<string, string?>
        {
            ["AttachmentId"] = attachment.Id.ToString(CultureInfo.InvariantCulture),
            ["RecordId"] = attachment.FfcRecordId.ToString(CultureInfo.InvariantCulture),
            ["Kind"] = attachment.Kind.ToString(),
            ["Caption"] = attachment.Caption,
            ["ContentType"] = attachment.ContentType,
            ["SizeBytes"] = attachment.SizeBytes.ToString(CultureInfo.InvariantCulture),
            ["ChecksumSha256"] = attachment.ChecksumSha256
        };

        try
        {
            await _storage.DeleteAsync(attachment);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to delete FFC attachment {AttachmentId} from record {RecordId}.",
                attachmentId,
                recordId);
            return FfcCommandResult.Invalid(
                "The attachment could not be removed. The existing file and record were retained where possible.");
        }

        await TryAuditAsync(
            "ProjectOfficeReports.FFC.AttachmentDeleted",
            attachment,
            originalFileName: null,
            snapshot);

        return FfcCommandResult.Ok(attachmentId, "Attachment removed.");
    }

    private async Task TryAuditAsync(
        string action,
        FfcAttachment attachment,
        string? originalFileName,
        IReadOnlyDictionary<string, string?>? suppliedData = null)
    {
        try
        {
            var http = _httpContextAccessor.HttpContext;
            var user = http?.User;
            var data = suppliedData is null
                ? new Dictionary<string, string?>
                {
                    ["AttachmentId"] = attachment.Id.ToString(CultureInfo.InvariantCulture),
                    ["RecordId"] = attachment.FfcRecordId.ToString(CultureInfo.InvariantCulture),
                    ["Kind"] = attachment.Kind.ToString(),
                    ["Caption"] = attachment.Caption,
                    ["ContentType"] = attachment.ContentType,
                    ["SizeBytes"] = attachment.SizeBytes.ToString(CultureInfo.InvariantCulture),
                    ["ChecksumSha256"] = attachment.ChecksumSha256,
                    ["OriginalFileName"] = originalFileName
                }
                : new Dictionary<string, string?>(suppliedData, StringComparer.Ordinal);

            await _audit.LogAsync(
                action,
                userId: user?.FindFirstValue(ClaimTypes.NameIdentifier),
                userName: user?.Identity?.Name,
                data: data,
                http: http);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to write FFC attachment audit entry {Action} for attachment {AttachmentId}.",
                action,
                attachment.Id);
        }
    }

    private static FfcAttachmentKind? ResolveKind(string? contentType)
        => contentType?.Trim().ToLowerInvariant() switch
        {
            "application/pdf" => FfcAttachmentKind.Pdf,
            "image/jpeg" or "image/png" or "image/webp" => FfcAttachmentKind.Photo,
            _ => null
        };

    private static IReadOnlyDictionary<string, string[]> Error(string key, string message)
        => new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [message] };
}
