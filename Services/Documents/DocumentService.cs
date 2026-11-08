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
using ProjectManagement.Data.Projects;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Notifications;
using ProjectManagement.Services.Projects;
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
    private readonly IProjectDocumentStorageResolver _storageResolver;
    private readonly IVirusScanner? _virusScanner;
    private readonly ILogger<DocumentService>? _logger;
    private readonly TransactionalNotificationQueue _notificationQueue;

    private static readonly string[] PdfMagic = { "%PDF-" };
    private static int _tempRequestTokenSeed = (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);

    public DocumentService(
        ApplicationDbContext db,
        IOptions<ProjectDocumentOptions> options,
        IUploadRootProvider uploadRootProvider,
        IClock clock,
        IAuditService audit,
        IDocumentNotificationService notifications,
        IProjectDocumentStorageResolver storageResolver,
        IVirusScanner? virusScanner = null,
        ILogger<DocumentService>? logger = null,
        TransactionalNotificationQueue? notificationQueue = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _storageResolver = storageResolver ?? throw new ArgumentNullException(nameof(storageResolver));
        _virusScanner = virusScanner;
        _logger = logger;
        _notificationQueue = notificationQueue ?? new TransactionalNotificationQueue(db);
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
        var normalizedContentType = NormalizeContentType(contentType, sanitizedName);
        EnsureMimeAllowed(normalizedContentType);

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var maxSizeBytes = GetMaxSizeBytes();
        var storageKey = BuildTempStorageKey(requestId, sanitizedName);
        var destinationPath = ResolveAbsolutePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        long totalBytes = 0;
        try
        {
            await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var headerBuffer = new byte[5];
            var headerRead = await content.ReadAsync(headerBuffer.AsMemory(0, headerBuffer.Length), cancellationToken);

            ValidateDocumentSignature(normalizedContentType, sanitizedName, headerBuffer, headerRead);

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
        var normalizedContentType = NormalizeContentType(contentType, sanitizedName);
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
            if (ProjectTotApplicabilityPolicy.GetIneligibilityReason(project) is { } applicabilityError)
            {
                throw new InvalidOperationException(applicabilityError);
            }

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
            IsArchived = false,
            OcrStatus = ProjectDocumentOcrStatus.Pending,
            OcrFailureReason = null,
            OcrLastTriedUtc = null
        };

        _db.ProjectDocuments.Add(document);
        await _db.SaveChangesAsync(cancellationToken);

        var storageKey = BuildDocumentStorageKey(projectId, stageId, document.Id, sanitizedName);
        var destinationPath = ResolveAbsolutePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        try
        {
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            SafeDelete(tempPath);
            throw;
        }

        // If this change, or a transaction it runs inside, does not commit, the file goes back
        // to its staging path so the request can be approved again.
        transaction.RegisterAfterRollback(_ =>
        {
            ReturnToStaging(destinationPath, tempPath);
            return Task.CompletedTask;
        });

        document.StorageKey = storageKey;
        await _notificationQueue.QueueAfterSaveAsync(
            "document publish",
            ct => _notifications.NotifyDocumentPublishedAsync(document, project, performedByUserId, ct),
            cancellationToken);
        await CommitThenAuditAsync(
            transaction,
            "document publish",
            () => Audit.Events.ProjectDocumentPublished(projectId, document.Id, performedByUserId, document.FileStamp)
                .WriteAsync(_audit),
            cancellationToken);

        document.Project = project;

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

        await _db.Entry(document).Reference(d => d.DocumentText).LoadAsync(cancellationToken);
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
        var normalizedContentType = NormalizeContentType(contentType, sanitizedName);
        EnsureMimeAllowed(normalizedContentType);

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);

        // The current file is moved aside rather than overwritten, so it can be put back if the
        // transaction does not commit. It is deleted only after the commit.
        FileQuarantineHandle? previousFile = null;
        try
        {
            previousFile = FileSystemQuarantine.StageFile(
                destinationPath,
                _uploadRootProvider.RootPath,
                "documents-replaced",
                documentId.ToString(CultureInfo.InvariantCulture));
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            RestorePreviousFile(previousFile, documentId);
            SafeDelete(tempPath);
            throw;
        }

        // If this change, or a transaction it runs inside, does not commit, the new file goes
        // back to its staging path so the request can be approved again, and the current file
        // is put back. The current file is removed only once the outermost transaction commits.
        transaction.RegisterAfterRollback(_ =>
        {
            ReturnToStaging(destinationPath, tempPath);
            RestorePreviousFile(previousFile, documentId);
            return Task.CompletedTask;
        });

        var finalizeAfterCommit = previousFile is not null && transaction.CanRegisterAfterCommit;
        if (finalizeAfterCommit)
        {
            transaction.RegisterAfterCommit(_ =>
            {
                FinalizePreviousFile(previousFile!, documentId);
                return Task.CompletedTask;
            });
        }

        await SaveReplacementAsync(
            document,
            project,
            sanitizedName,
            normalizedContentType,
            fileSize,
            uploadedByUserId,
            performedByUserId,
            cancellationToken);
        await CommitThenAuditAsync(
            transaction,
            "document replace",
            () => Audit.Events.ProjectDocumentReplaced(document.ProjectId, document.Id, performedByUserId, document.FileStamp)
                .WriteAsync(_audit),
            cancellationToken);

        if (previousFile is not null && !finalizeAfterCommit)
        {
            // Inside a transaction this service did not create, the outer commit cannot be
            // observed; the previous file is removed once this change's savepoint is released.
            FinalizePreviousFile(previousFile, documentId);
        }

        document.Project = project;

        return document;
    }

    private async Task SaveReplacementAsync(
        ProjectDocument document,
        Project project,
        string sanitizedName,
        string normalizedContentType,
        long fileSize,
        string uploadedByUserId,
        string performedByUserId,
        CancellationToken cancellationToken)
    {
        document.OriginalFileName = sanitizedName;
        document.ContentType = normalizedContentType;
        document.FileSize = fileSize;
        document.FileStamp += 1;
        document.UploadedByUserId = uploadedByUserId;
        document.UploadedAtUtc = _clock.UtcNow;
        document.OcrStatus = ProjectDocumentOcrStatus.Pending;
        document.OcrFailureReason = null;
        document.OcrLastTriedUtc = null;

        if (document.DocumentText is not null)
        {
            document.DocumentText.OcrText = null;
            document.DocumentText.UpdatedAtUtc = _clock.UtcNow;
        }

        if (document.Status == ProjectDocumentStatus.SoftDeleted)
        {
            document.Status = ProjectDocumentStatus.Published;
            document.IsArchived = false;
            document.ArchivedAtUtc = null;
            document.ArchivedByUserId = null;
        }

        await _notificationQueue.QueueAfterSaveAsync(
            "document replace",
            ct => _notifications.NotifyDocumentReplacedAsync(document, project, performedByUserId, ct),
            cancellationToken);
    }

    private void FinalizePreviousFile(FileQuarantineHandle previousFile, int documentId)
    {
        try
        {
            FileSystemQuarantine.FinalizeDeletion(previousFile);
        }
        catch (Exception cleanupError)
        {
            _logger?.LogWarning(
                cleanupError,
                "Document {DocumentId} was replaced, but its previous file could not be removed. Quarantine reference: {Reference}",
                documentId,
                FileSystemQuarantine.GetSafeReference(previousFile));
        }
    }

    /// <summary>
    /// Commits <paramref name="transaction"/> and records the audit entry once the change is
    /// durable. When this service runs inside another transaction (a document approval), the
    /// change is durable only when that transaction commits, so the audit entry waits for it.
    /// </summary>
    private async Task CommitThenAuditAsync(
        RelationalTransactionScope transaction,
        string operation,
        Func<Task> audit,
        CancellationToken cancellationToken)
    {
        if (transaction.IsNested && transaction.CanRegisterAfterCommit)
        {
            transaction.RegisterAfterCommit(async _ =>
            {
                try
                {
                    await audit();
                }
                catch (Exception error)
                {
                    _logger?.LogError(error, "The audit entry for {Operation} could not be written after commit.", operation);
                }
            });
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await transaction.CommitAsync(cancellationToken);
        await audit();
    }

    // Moves a file that was placed for an uncommitted change back to its staging path.
    private void ReturnToStaging(string placedPath, string stagingPath)
    {
        try
        {
            if (File.Exists(placedPath) && !File.Exists(stagingPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
                File.Move(placedPath, stagingPath);
            }
        }
        catch (Exception error)
        {
            _logger?.LogError(error, "A file placed for an uncommitted document change could not be returned to staging.");
        }
    }

    private void RestorePreviousFile(FileQuarantineHandle? previousFile, int documentId)
    {
        if (previousFile is null)
        {
            return;
        }

        try
        {
            if (File.Exists(previousFile.OriginalPath) && File.Exists(previousFile.QuarantinePath))
            {
                // The new file is still in place; the original takes its slot back.
                File.Delete(previousFile.OriginalPath);
            }

            FileSystemQuarantine.Restore(previousFile);
        }
        catch (Exception restoreError)
        {
            _logger?.LogCritical(
                restoreError,
                "Document {DocumentId} replacement failed and its previous file could not be restored. Quarantine reference: {Reference}",
                documentId,
                FileSystemQuarantine.GetSafeReference(previousFile));
        }
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

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        await _notificationQueue.QueueAfterSaveAsync(
            "document archive",
            ct => _notifications.NotifyDocumentArchivedAsync(document, project, performedByUserId, ct),
            cancellationToken);
        await CommitThenAuditAsync(
            transaction,
            "document archive",
            () => Audit.Events.ProjectDocumentRemoved(document.ProjectId, document.Id, performedByUserId)
                .WriteAsync(_audit),
            cancellationToken);

        document.Project = project;

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

        // The restore, its notification and its audit entry commit in one transaction
        // (docs/ux/admin-review-4-fixes.md, PR 2). A failed audit write rolls the restore back,
        // so the document stays in the recycle bin and a retry finds it there; a document is
        // never restored without a record of who restored it. A notification failure is still
        // contained by the queue's savepoint and never fails the restore.
        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        try
        {
            await _notificationQueue.QueueAfterSaveAsync(
                "document restore",
                ct => _notifications.NotifyDocumentRestoredAsync(document, project, performedByUserId, ct),
                cancellationToken);

            await Audit.Events.ProjectDocumentRestored(document.ProjectId, document.Id, performedByUserId)
                .WriteAsync(_audit);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Nothing was committed; leave no half-restored document tracked in this context, so
            // the next document in a bulk restore does not save it again.
            _db.Entry(document).State = EntityState.Detached;
            throw;
        }

        document.Project = project;

        return document;
    }

    // SECTION: OCR management helpers
    public async Task<ProjectDocument> RetryOcrAsync(
        int documentId,
        string performedByUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(performedByUserId))
        {
            throw new ArgumentException("Performed by user id is required.", nameof(performedByUserId));
        }

        var document = await _db.ProjectDocuments
            .Include(d => d.DocumentText)
            .FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);

        if (document == null)
        {
            throw new InvalidOperationException($"Document {documentId} was not found.");
        }

        if (document.Status != ProjectDocumentStatus.Published || document.IsArchived)
        {
            throw new InvalidOperationException("Only active project documents can be queued for OCR.");
        }

        document.OcrStatus = ProjectDocumentOcrStatus.Pending;
        document.OcrFailureReason = null;
        document.OcrLastTriedUtc = null;

        if (document.DocumentText is not null)
        {
            document.DocumentText.OcrText = null;
            document.DocumentText.UpdatedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events.ProjectDocumentOcrRequeued(document.ProjectId, document.Id, performedByUserId)
            .WriteAsync(_audit);

        return document;
    }

    public async Task HardDeleteAsync(
        int documentId,
        string performedByUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(performedByUserId))
        {
            throw new ArgumentException("Performed by user id is required.", nameof(performedByUserId));
        }

        var document = await _db.ProjectDocuments
            .Include(candidate => candidate.Project)
            .FirstOrDefaultAsync(candidate => candidate.Id == documentId, cancellationToken);
        if (document is null)
        {
            return;
        }

        // Permanent deletion is only for documents in the recycle bin. A document restored
        // after the caller listed it (or any id that never was in the bin) must survive. The
        // row version on ProjectDocument covers a restore that lands after this check.
        if (document.Status != ProjectDocumentStatus.SoftDeleted)
        {
            throw new InvalidOperationException(
                "Only documents in the recycle bin can be permanently deleted.");
        }

        var project = document.Project;
        var projectId = document.ProjectId;
        var storagePath = ResolveAbsolutePath(document.StorageKey);
        FileQuarantineHandle? quarantinedFile = null;

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        try
        {
            quarantinedFile = FileSystemQuarantine.StageFile(
                storagePath,
                _uploadRootProvider.RootPath,
                "documents",
                documentId.ToString(CultureInfo.InvariantCulture));

            _db.ProjectDocuments.Remove(document);
            await _db.SaveChangesAsync(cancellationToken);

            await Audit.Events.ProjectDocumentHardDeleted(projectId, documentId, performedByUserId)
                .WriteAsync(_audit);

            if (project is not null)
            {
                await _notificationQueue.QueueAfterSaveAsync(
                    "document hard delete",
                    ct => _notifications.NotifyDocumentDeletedAsync(document, project, performedByUserId, ct),
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // The request token is ignored so a cancelled request still restores the file.
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();

            if (quarantinedFile is not null)
            {
                try
                {
                    FileSystemQuarantine.Restore(quarantinedFile);
                }
                catch (Exception restoreError)
                {
                    var reference = FileSystemQuarantine.GetSafeReference(quarantinedFile);
                    _logger?.LogCritical(
                        restoreError,
                        "Document {DocumentId} hard delete failed and its quarantined file could not be restored. Quarantine reference: {Reference}",
                        documentId,
                        reference);
                    throw new InvalidOperationException(
                        "The document deletion failed and its file could not be restored automatically.",
                        restoreError);
                }
            }

            throw;
        }

        if (quarantinedFile is not null)
        {
            try
            {
                FileSystemQuarantine.FinalizeDeletion(quarantinedFile);
                var originalDirectory = Path.GetDirectoryName(storagePath);
                TryDeleteDirectoryIfEmpty(originalDirectory);
                if (!string.IsNullOrWhiteSpace(originalDirectory))
                {
                    TryDeleteDirectoryIfEmpty(Path.GetDirectoryName(originalDirectory));
                }
            }
            catch (Exception cleanupError)
            {
                var reference = FileSystemQuarantine.GetSafeReference(quarantinedFile);
                _logger?.LogError(
                    cleanupError,
                    "Document {DocumentId} database record was deleted, but final deletion of the quarantined file failed. Quarantine reference: {Reference}",
                    documentId,
                    reference);

                try
                {
                    await _audit.LogAsync(
                        "Project.DocumentHardDeleteCleanupPending",
                        message: $"Document {documentId} was deleted, but its quarantined file requires cleanup.",
                        level: "Warning",
                        userId: performedByUserId,
                        data: new System.Collections.Generic.Dictionary<string, string?>
                        {
                            ["ProjectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                            ["DocumentId"] = documentId.ToString(CultureInfo.InvariantCulture),
                            ["QuarantineReference"] = reference
                        });
                }
                catch (Exception auditError)
                {
                    _logger?.LogCritical(
                        auditError,
                        "Document {DocumentId} file cleanup is pending and the warning audit could not be recorded. Quarantine reference: {Reference}",
                        documentId,
                        reference);
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(document.StorageKey))
        {
            _logger?.LogWarning(
                "Document {DocumentId} was hard-deleted, but its storage file was not present at deletion time.",
                documentId);
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

    private string BuildTempStorageKey(int requestId, string originalFileName)
    {
        var requestSegment = requestId.ToString(CultureInfo.InvariantCulture);
        var extension = GetNormalizedExtension(originalFileName);
        var fileName = string.IsNullOrWhiteSpace(extension) ? "file" : $"file.{extension}";
        return NormalizeStorageKey(
            _options.ProjectsSubpath,
            _options.TempSubPath,
            "requests",
            requestSegment,
            fileName);
    }

    private string BuildDocumentStorageKey(int projectId, int? stageId, int documentId, string originalFileName)
    {
        var projectSegment = projectId.ToString(CultureInfo.InvariantCulture);
        var stageSegment = stageId?.ToString(CultureInfo.InvariantCulture) ?? "general";
        var documentSegment = documentId.ToString(CultureInfo.InvariantCulture);
        var extension = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".pdf";
        }

        var uniqueName = $"{documentSegment}-{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

        return NormalizeStorageKey(
            _options.ProjectsSubpath,
            projectSegment,
            _options.StorageSubPath,
            "stages",
            stageSegment,
            uniqueName);
    }

    private string NormalizeStorageKey(params string[] segments)
    {
        var relative = Path.Combine(segments.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray());
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private string ResolveAbsolutePath(string storageKey)
    {
        return _storageResolver.ResolveAbsolutePath(storageKey);
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

    // SECTION: File signature validation
    private static bool IsPdfMagic(byte[] buffer, int count)
    {
        if (count < 5)
        {
            return false;
        }

        var text = System.Text.Encoding.ASCII.GetString(buffer, 0, 5);
        return PdfMagic.Any(m => text.StartsWith(m, StringComparison.Ordinal));
    }

    private static bool IsZipMagic(byte[] buffer, int count)
    {
        if (count < 2)
        {
            return false;
        }

        return buffer[0] == 0x50 && buffer[1] == 0x4B;
    }

    private static void ValidateDocumentSignature(string contentType, string originalFileName, byte[] headerBuffer, int headerRead)
    {
        if (!DocumentTypeValidation.SignatureRules.TryGetValue(contentType, out var rule))
        {
            return;
        }

        var extension = GetNormalizedExtension(originalFileName);
        if (!string.Equals(extension, rule.Extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The uploaded file does not match the allowed types.");
        }

        var signatureValid = rule.SignatureType switch
        {
            DocumentSignatureType.Pdf => IsPdfMagic(headerBuffer, headerRead),
            DocumentSignatureType.Zip => IsZipMagic(headerBuffer, headerRead),
            _ => true
        };

        if (!signatureValid)
        {
            throw new InvalidOperationException("The uploaded file does not match the allowed types.");
        }
    }

    private static string GetNormalizedExtension(string? fileName)
    {
        var extension = string.IsNullOrWhiteSpace(fileName)
            ? string.Empty
            : Path.GetExtension(fileName);
        return extension.TrimStart('.').ToLowerInvariant();
    }

    private string SanitizeOriginalFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "file";
        }

        var safeName = Path.GetFileName(fileName.Trim());
        return string.IsNullOrWhiteSpace(safeName) ? "file" : safeName;
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

    private string NormalizeContentType(string? contentType, string originalFileName)
    {
        var normalized = string.IsNullOrWhiteSpace(contentType)
            ? string.Empty
            : contentType.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            var extension = GetNormalizedExtension(originalFileName);
            if (DocumentTypeValidation.TryGetContentTypeForExtension(extension, out var inferredContentType))
            {
                return inferredContentType;
            }

            return "application/octet-stream";
        }

        return normalized;
    }
}
