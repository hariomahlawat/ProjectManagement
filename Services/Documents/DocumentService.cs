using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Services.Documents;

public sealed class DocumentService : IDocumentService
{
    private readonly ApplicationDbContext _db;
    private readonly ProjectDocumentOptions _options;
    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IDocumentNotificationService _notifications;
    private readonly IVirusScanner? _virusScanner;
    private readonly ILogger<DocumentService>? _logger;

    private static readonly string[] PdfMagic = { "%PDF-" };
    private static int _tempRequestTokenSeed = (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);

    public DocumentService(
        ApplicationDbContext db,
        IOptions<ProjectDocumentOptions> options,
        IUploadRootProvider uploadRootProvider,
        IClock clock,
        IAuditService audit,
        IDocumentNotificationService notifications,
        IVirusScanner? virusScanner = null,
        ILogger<DocumentService>? logger = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _virusScanner = virusScanner;
        _logger = logger;
    }

    public int CreateTempRequestToken()
    {
        var next = Interlocked.Increment(ref _tempRequestTokenSeed);
        if (next <= 0)
        {
            Interlocked.Exchange(ref _tempRequestTokenSeed, 1);
            next = Interlocked.Increment(ref _tempRequestTokenSeed);
        }

        return next;
    }

    public async Task<DocumentFileDescriptor> SaveTempAsync(
        int requestId,
        Stream content,
        string originalFileName,
        string? contentType,
        CancellationToken cancellationToken)
    {
        if (requestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId));
        }

        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        var sanitizedName = SanitizeOriginalFileName(originalFileName);
        var normalizedContentType = NormalizeContentType(contentType);
        EnsureMimeAllowed(normalizedContentType);

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var maxSizeBytes = GetMaxSizeBytes();
        var storageKey = BuildTempStorageKey(requestId);
        var destinationPath = ResolveAbsolutePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        long totalBytes = 0;
        try
        {
            await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var headerBuffer = new byte[5];
            var headerRead = await content.ReadAsync(headerBuffer.AsMemory(0, headerBuffer.Length), cancellationToken);

            if (!IsPdfMagic(headerBuffer, headerRead))
            {
                throw new InvalidOperationException("The uploaded file is not a valid PDF document.");
            }

            await destination.WriteAsync(headerBuffer.AsMemory(0, headerRead), cancellationToken);
            totalBytes += headerRead;

            if (totalBytes > maxSizeBytes)
            {
                throw SizeExceededException();
            }

            var buffer = new byte[81920];
            while (true)
            {
                var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                totalBytes += read;
                if (totalBytes > maxSizeBytes)
                {
                    throw SizeExceededException();
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await destination.FlushAsync(cancellationToken);
        }
        catch
        {
            SafeDelete(destinationPath);
            throw;
        }

        try
        {
            if (_options.EnableVirusScan)
            {
                if (_virusScanner == null)
                {
                    throw new InvalidOperationException("Virus scanning is enabled but no scanner is configured.");
                }

                await using var scanStream = File.OpenRead(destinationPath);
                await _virusScanner.ScanAsync(scanStream, sanitizedName, cancellationToken);
            }
        }
        catch
        {
            SafeDelete(destinationPath);
            throw;
        }

        return new DocumentFileDescriptor(storageKey, sanitizedName, totalBytes, normalizedContentType);
    }

    public async Task<ProjectDocument> PublishNewAsync(
        int projectId,
        int? stageId,
        int? totId,
        string nomenclature,
        string tempStorageKey,
        string originalFileName,
        long fileSize,
        string contentType,
        string uploadedByUserId,
        string performedByUserId,
        CancellationToken cancellationToken)
    {
        if (fileSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSize));
        }

        if (string.IsNullOrWhiteSpace(uploadedByUserId))
        {
            throw new ArgumentException("Uploaded by user id is required.", nameof(uploadedByUserId));
        }

        var sanitizedName = SanitizeOriginalFileName(originalFileName);
        var normalizedContentType = NormalizeContentType(contentType);
        EnsureMimeAllowed(normalizedContentType);

        var tempPath = ResolveAbsolutePath(tempStorageKey);
        if (!File.Exists(tempPath))
        {
            throw new FileNotFoundException("Temporary file not found.", tempPath);
        }

        var project = await _db.Projects
            .Include(p => p.Tot)
            .SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new InvalidOperationException($"Project {projectId} was not found.");

        if (totId.HasValue)
        {
            if (project.Tot is null || project.Tot.Id != totId.Value)
            {
                throw new InvalidOperationException("Selected Transfer of Technology record was not found for this project.");
            }

            if (project.Tot.Status == ProjectTotStatus.NotRequired)
            {
                throw new InvalidOperationException("Transfer of Technology is not required for this project.");
            }
        }

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);

        var now = _clock.UtcNow;
        var document = new ProjectDocument
        {
            ProjectId = projectId,
            StageId = stageId,
            TotId = totId,
            Title = nomenclature?.Trim() ?? string.Empty,
            StorageKey = string.Empty,
            OriginalFileName = sanitizedName,
            ContentType = normalizedContentType,
            FileSize = fileSize,
            Status = ProjectDocumentStatus.Published,
            FileStamp = 1,
            UploadedByUserId = uploadedByUserId,
            UploadedAtUtc = now,
            ArchivedAtUtc = null,
            ArchivedByUserId = null,
            IsArchived = false
        };

        _db.ProjectDocuments.Add(document);
        await _db.SaveChangesAsync(cancellationToken);

        var storageKey = BuildDocumentStorageKey(projectId, stageId, document.Id);
        var destinationPath = ResolveAbsolutePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        try
        {
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            SafeDelete(tempPath);
            throw;
        }

        document.StorageKey = storageKey;
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await Audit.Events.ProjectDocumentPublished(projectId, document.Id, performedByUserId, document.FileStamp)
            .WriteAsync(_audit);

        document.Project = project;
        await _notifications.NotifyDocumentPublishedAsync(document, project, performedByUserId, cancellationToken);

        return document;
    }

    public async Task<ProjectDocument> OverwriteAsync(
        int documentId,
        string tempStorageKey,
        string originalFileName,
        long fileSize,
        string contentType,
        string uploadedByUserId,
        string performedByUserId,
        CancellationToken cancellationToken)
    {
        if (fileSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSize));
        }

        if (string.IsNullOrWhiteSpace(uploadedByUserId))
        {
            throw new ArgumentException("Uploaded by user id is required.", nameof(uploadedByUserId));
        }

        var document = await _db.ProjectDocuments.FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);
        if (document == null)
        {
            throw new InvalidOperationException($"Document {documentId} was not found.");
        }

        await _db.Entry(document).Reference(d => d.Project).LoadAsync(cancellationToken);
        var project = document.Project
            ?? await _db.Projects.SingleOrDefaultAsync(p => p.Id == document.ProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"Project {document.ProjectId} was not found.");

        var tempPath = ResolveAbsolutePath(tempStorageKey);
        if (!File.Exists(tempPath))
        {
            throw new FileNotFoundException("Temporary file not found.", tempPath);
        }

        var destinationPath = ResolveAbsolutePath(document.StorageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        var sanitizedName = SanitizeOriginalFileName(originalFileName);
        var normalizedContentType = NormalizeContentType(contentType);
        EnsureMimeAllowed(normalizedContentType);

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);

        try
        {
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            SafeDelete(tempPath);
            throw;
        }

        document.OriginalFileName = sanitizedName;
        document.ContentType = normalizedContentType;
        document.FileSize = fileSize;
        document.FileStamp += 1;
        document.UploadedByUserId = uploadedByUserId;
        document.UploadedAtUtc = _clock.UtcNow;

        if (document.Status == ProjectDocumentStatus.SoftDeleted)
        {
            document.Status = ProjectDocumentStatus.Published;
            document.IsArchived = false;
            document.ArchivedAtUtc = null;
            document.ArchivedByUserId = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await Audit.Events.ProjectDocumentReplaced(document.ProjectId, document.Id, performedByUserId, document.FileStamp)
            .WriteAsync(_audit);

        document.Project = project;
        await _notifications.NotifyDocumentReplacedAsync(document, project, performedByUserId, cancellationToken);

        return document;
    }

    public async Task<ProjectDocument> SoftDeleteAsync(
        int documentId,
        string performedByUserId,
        CancellationToken cancellationToken)
    {
        var document = await _db.ProjectDocuments.FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);
        if (document == null)
        {
            throw new InvalidOperationException($"Document {documentId} was not found.");
        }

        await _db.Entry(document).Reference(d => d.Project).LoadAsync(cancellationToken);
        var project = document.Project
            ?? await _db.Projects.SingleOrDefaultAsync(p => p.Id == document.ProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"Project {document.ProjectId} was not found.");

        if (document.Status == ProjectDocumentStatus.SoftDeleted)
        {
            return document;
        }

        document.Status = ProjectDocumentStatus.SoftDeleted;
        document.IsArchived = true;
        document.ArchivedAtUtc = _clock.UtcNow;
        document.ArchivedByUserId = performedByUserId;

        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events.ProjectDocumentRemoved(document.ProjectId, document.Id, performedByUserId)
            .WriteAsync(_audit);

        document.Project = project;
        await _notifications.NotifyDocumentArchivedAsync(document, project, performedByUserId, cancellationToken);

        return document;
    }

    public async Task<ProjectDocument> RestoreAsync(
        int documentId,
        string performedByUserId,
        CancellationToken cancellationToken)
    {
        var document = await _db.ProjectDocuments.FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);
        if (document == null)
        {
            throw new InvalidOperationException($"Document {documentId} was not found.");
        }

        await _db.Entry(document).Reference(d => d.Project).LoadAsync(cancellationToken);
        var project = document.Project
            ?? await _db.Projects.SingleOrDefaultAsync(p => p.Id == document.ProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"Project {document.ProjectId} was not found.");

        if (document.Status == ProjectDocumentStatus.Published)
        {
            return document;
        }

        document.Status = ProjectDocumentStatus.Published;
        document.IsArchived = false;
        document.ArchivedAtUtc = null;
        document.ArchivedByUserId = null;

        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events.ProjectDocumentRestored(document.ProjectId, document.Id, performedByUserId)
            .WriteAsync(_audit);

        document.Project = project;
        await _notifications.NotifyDocumentRestoredAsync(document, project, performedByUserId, cancellationToken);

        return document;
    }

    public async Task HardDeleteAsync(
        int documentId,
        string performedByUserId,
        CancellationToken cancellationToken)
    {
        var document = await _db.ProjectDocuments.FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);
        if (document == null)
        {
            return;
        }

        await _db.Entry(document).Reference(d => d.Project).LoadAsync(cancellationToken);
        var project = document.Project
            ?? await _db.Projects.SingleOrDefaultAsync(p => p.Id == document.ProjectId, cancellationToken);

        var storageKey = document.StorageKey;
        var projectId = document.ProjectId;

        _db.ProjectDocuments.Remove(document);
        await _db.SaveChangesAsync(cancellationToken);

        var path = ResolveAbsolutePath(storageKey);
        SafeDelete(path);

        var directory = Path.GetDirectoryName(path);
        TryDeleteDirectoryIfEmpty(directory);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            TryDeleteDirectoryIfEmpty(Path.GetDirectoryName(directory));
        }

        await Audit.Events.ProjectDocumentHardDeleted(projectId, documentId, performedByUserId)
            .WriteAsync(_audit);

        if (project is not null)
        {
            document.Project = project;
            await _notifications.NotifyDocumentDeletedAsync(document, project, performedByUserId, cancellationToken);
        }
    }

    public async Task<DocumentStreamResult?> OpenStreamAsync(
        int documentId,
        CancellationToken cancellationToken)
    {
        var document = await _db.ProjectDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == documentId && x.Status == ProjectDocumentStatus.Published, cancellationToken);

        if (document == null)
        {
            return null;
        }

        var path = ResolveAbsolutePath(document.StorageKey);
        if (!File.Exists(path))
        {
            _logger?.LogWarning("Document {DocumentId} storage file missing at {Path}", documentId, path);
            return null;
        }

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new DocumentStreamResult(stream, document.OriginalFileName, document.ContentType, document.FileSize, document.FileStamp);
    }

    public Task DeleteTempAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return Task.CompletedTask;
        }

        var path = ResolveAbsolutePath(storageKey);
        SafeDelete(path);
        var requestDirectory = Path.GetDirectoryName(path);
        TryDeleteDirectoryIfEmpty(requestDirectory);
        if (!string.IsNullOrWhiteSpace(requestDirectory))
        {
            TryDeleteDirectoryIfEmpty(Path.GetDirectoryName(requestDirectory));
        }
        return Task.CompletedTask;
    }

    private string BuildTempStorageKey(int requestId)
    {
        var requestSegment = requestId.ToString(CultureInfo.InvariantCulture);
        return NormalizeStorageKey(
            _options.ProjectsSubpath,
            _options.TempSubPath,
            "requests",
            requestSegment,
            "file.pdf");
    }

    private string BuildDocumentStorageKey(int projectId, int? stageId, int documentId)
    {
        var projectSegment = projectId.ToString(CultureInfo.InvariantCulture);
        var stageSegment = stageId?.ToString(CultureInfo.InvariantCulture) ?? "general";
        var documentSegment = documentId.ToString(CultureInfo.InvariantCulture);

        return NormalizeStorageKey(
            _options.ProjectsSubpath,
            projectSegment,
            _options.StorageSubPath,
            "stages",
            stageSegment,
            documentSegment,
            "file.pdf");
    }

    private string NormalizeStorageKey(params string[] segments)
    {
        var relative = Path.Combine(segments.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray());
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private string ResolveAbsolutePath(string storageKey)
    {
        var normalized = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.Combine(_uploadRootProvider.RootPath, normalized);
        var full = Path.GetFullPath(combined);
        var root = Path.GetFullPath(_uploadRootProvider.RootPath);

        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid storage key.");
        }

        return full;
    }

    private static void SafeDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup errors.
        }
    }

    private static void TryDeleteDirectoryIfEmpty(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory, recursive: false);
            }
        }
        catch
        {
            // Swallow cleanup failures.
        }
    }

    private static bool IsPdfMagic(byte[] buffer, int count)
    {
        if (count < 5)
        {
            return false;
        }

        var text = System.Text.Encoding.ASCII.GetString(buffer, 0, 5);
        return PdfMagic.Any(m => text.StartsWith(m, StringComparison.Ordinal));
    }

    private string SanitizeOriginalFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "file.pdf";
        }

        var safeName = Path.GetFileName(fileName.Trim());
        return string.IsNullOrWhiteSpace(safeName) ? "file.pdf" : safeName;
    }

    private long GetMaxSizeBytes()
    {
        if (_options.MaxSizeMb <= 0)
        {
            return long.MaxValue;
        }

        return _options.MaxSizeMb * 1024L * 1024L;
    }

    private InvalidOperationException SizeExceededException()
        => new($"The uploaded file exceeds the maximum allowed size of {_options.MaxSizeMb} MB.");

    private void EnsureMimeAllowed(string contentType)
    {
        if (_options.AllowedMimeTypes is { Count: > 0 } allowed && !allowed.Contains(contentType))
        {
            throw new InvalidOperationException($"The content type '{contentType}' is not allowed.");
        }
    }

    private string NormalizeContentType(string? contentType)
    {
        var normalized = string.IsNullOrWhiteSpace(contentType)
            ? "application/pdf"
            : contentType.Trim();
        return normalized;
    }
}
