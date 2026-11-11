using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Services.ItProjects;

/// <summary>
/// The only writer of IT project attachments. An upload is validated (name, type, size, content),
/// scanned, stored, and only then recorded with its audit entry in one transaction; when the
/// record cannot be written the stored file is removed again, so file and row never disagree.
/// </summary>
public sealed class ItProjectAttachmentService : IItProjectAttachmentService
{
    public const string NotFoundMessage = "The file could not be found.";
    public const string ScanFailedMessage = "The file failed the virus scan.";

    private readonly ApplicationDbContext _db;
    private readonly IItProjectAttachmentStorage _storage;
    private readonly ItProjectAttachmentValidator _validator;
    private readonly IFileScanner _scanner;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly ILogger<ItProjectAttachmentService> _logger;

    public ItProjectAttachmentService(
        ApplicationDbContext db,
        IItProjectAttachmentStorage storage,
        ItProjectAttachmentValidator validator,
        IFileScanner scanner,
        IAuditService audit,
        IClock clock,
        ILogger<ItProjectAttachmentService>? logger = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? NullLogger<ItProjectAttachmentService>.Instance;
    }

    public long MaxSizeBytes => _validator.MaxSizeBytes;

    public Task<IReadOnlyList<ItProjectAttachmentView>> ListAsync(int itProjectId, CancellationToken cancellationToken = default)
        => ListAsync(itProjectId, ItProjectModules.It, cancellationToken);

    public async Task<IReadOnlyList<ItProjectAttachmentView>> ListAsync(int itProjectId, string module, CancellationToken cancellationToken = default)
    {
        var rows = await _db.ItProjectAttachments.AsNoTracking()
            .Where(attachment => attachment.ItProjectId == itProjectId && !attachment.IsDeleted && attachment.ItProject != null && attachment.ItProject.Module == module)
            .OrderByDescending(attachment => attachment.UploadedAtUtc)
            .ThenBy(attachment => attachment.OriginalFileName)
            .Select(attachment => new
            {
                attachment.Id,
                attachment.OriginalFileName,
                attachment.ContentType,
                attachment.SizeBytes,
                attachment.UploadedByUserId,
                attachment.UploadedAtUtc
            })
            .ToListAsync(cancellationToken);

        var userIds = rows.Select(row => row.UploadedByUserId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.FullName, user.UserName })
            .ToDictionaryAsync(user => user.Id, user => string.IsNullOrWhiteSpace(user.FullName) ? user.UserName ?? user.Id : user.FullName, cancellationToken);

        return rows
            .Select(row => new ItProjectAttachmentView(
                row.Id,
                row.OriginalFileName,
                row.ContentType,
                row.SizeBytes,
                row.UploadedByUserId,
                names.TryGetValue(row.UploadedByUserId, out var name) ? name : row.UploadedByUserId,
                row.UploadedAtUtc))
            .ToList();
    }

    public async Task<(ItProjectCommandResult Result, Guid AttachmentId)> UploadAsync(int itProjectId, IFormFile file, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        EnsureActor(actor);
        if (file is null)
        {
            return (ItProjectCommandResult.Fail("Choose a file."), Guid.Empty);
        }

        // Some browsers send a full client path; only the file name is kept.
        var fileName = Path.GetFileName((file.FileName ?? string.Empty).Replace('\\', '/')).Trim();
        var contentType = ItProjectAttachmentValidator.ResolveContentType(fileName, file.ContentType);
        var errors = _validator.Validate(fileName, contentType, file.Length);
        if (errors.Count > 0)
        {
            return (ItProjectCommandResult.Fail(errors), Guid.Empty);
        }

        var projectExists = await _db.ItProjects.AnyAsync(project => project.Id == itProjectId && project.Module == actor.Module && !project.IsDeleted, cancellationToken);
        if (!projectExists)
        {
            return (ItProjectCommandResult.Fail(ItProjectCommandService.NotFoundMessage), Guid.Empty);
        }

        // The upload is buffered in a temporary file, not in buffer: the limit is the configured
        // MaxSizeMb (100 MB by default), and the content check, scan and store each read it again.
        await using var buffer = new FileStream(
            Path.Combine(Path.GetTempPath(), $"prism-itp-{Guid.NewGuid():N}.upload"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        await file.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        if (buffer.Length != file.Length)
        {
            // The declared length was checked; the bytes that arrived must be checked too.
            var sizeErrors = _validator.Validate(fileName, contentType, buffer.Length);
            if (sizeErrors.Count > 0)
            {
                return (ItProjectCommandResult.Fail(sizeErrors), Guid.Empty);
            }
        }

        if (!_validator.ContentMatches(contentType, buffer))
        {
            return (ItProjectCommandResult.Fail(ItProjectAttachmentValidator.ContentMismatchMessage), Guid.Empty);
        }

        try
        {
            await _scanner.ScanOrThrowAsync(buffer, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "IT project {ItProjectId} attachment {FileName} failed the scan.", itProjectId, fileName);
            return (ItProjectCommandResult.Fail(ScanFailedMessage), Guid.Empty);
        }

        buffer.Position = 0;
        string storageKey;
        try
        {
            storageKey = await _storage.SaveAsync(itProjectId, fileName, buffer, cancellationToken);
        }
        catch (ItProjectAttachmentRejectedException exception)
        {
            return (ItProjectCommandResult.Fail(exception.Message), Guid.Empty);
        }

        var attachment = new ItProjectAttachment
        {
            ItProjectId = itProjectId,
            OriginalFileName = fileName,
            StorageKey = storageKey,
            ContentType = contentType,
            SizeBytes = buffer.Length,
            UploadedByUserId = actor.UserId,
            UploadedAtUtc = UtcNow()
        };

        try
        {
            await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
            _db.ItProjectAttachments.Add(attachment);
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync("ItProjects.AttachmentUploaded", userId: actor.UserId, userName: actor.UserName, data: Data(attachment));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Without its row the stored file would be an orphan.
            _db.Entry(attachment).State = EntityState.Detached;
            await TryDeleteStoredFileAsync(storageKey, itProjectId);
            throw;
        }

        return (ItProjectCommandResult.Ok(), attachment.Id);
    }

    public Task<ItProjectAttachmentDownload?> OpenAsync(int itProjectId, Guid attachmentId, CancellationToken cancellationToken = default)
        => OpenAsync(itProjectId, attachmentId, ItProjectModules.It, cancellationToken);

    public async Task<ItProjectAttachmentDownload?> OpenAsync(int itProjectId, Guid attachmentId, string module, CancellationToken cancellationToken = default)
    {
        var attachment = await LoadAsync(itProjectId, attachmentId, module, cancellationToken);
        if (attachment is null)
        {
            return null;
        }

        try
        {
            var stream = await _storage.OpenReadAsync(attachment.StorageKey, cancellationToken);
            return new ItProjectAttachmentDownload(stream, attachment.OriginalFileName, attachment.ContentType);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            _logger.LogWarning(exception, "IT project {ItProjectId} attachment {AttachmentId} has no file at {StorageKey}.", itProjectId, attachmentId, attachment.StorageKey);
            return null;
        }
    }

    public async Task<ItProjectCommandResult> DeleteAsync(int itProjectId, Guid attachmentId, ItProjectActor actor, CancellationToken cancellationToken = default)
    {
        EnsureActor(actor);

        var attachment = await LoadAsync(itProjectId, attachmentId, actor.Module, cancellationToken);
        if (attachment is null)
        {
            return ItProjectCommandResult.Fail(NotFoundMessage);
        }

        attachment.IsDeleted = true;
        attachment.DeletedAtUtc = UtcNow();
        attachment.DeletedByUserId = actor.UserId;

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("ItProjects.AttachmentDeleted", userId: actor.UserId, userName: actor.UserName, data: Data(attachment));
        await transaction.CommitAsync(cancellationToken);
        return ItProjectCommandResult.Ok();
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>
    /// The live attachment with that id on that live project. An id from another project, or
    /// from a soft-deleted project, is "not found": the route's project is the one the caller
    /// has been authorised for (engineering invariant 6).
    /// </summary>
    private Task<ItProjectAttachment?> LoadAsync(int itProjectId, Guid attachmentId, string module, CancellationToken cancellationToken)
        => _db.ItProjectAttachments
            .FirstOrDefaultAsync(attachment =>
                attachment.Id == attachmentId &&
                attachment.ItProjectId == itProjectId &&
                !attachment.IsDeleted &&
                attachment.ItProject != null &&
                attachment.ItProject.Module == module &&
                !attachment.ItProject.IsDeleted,
                cancellationToken);

    private async Task TryDeleteStoredFileAsync(string storageKey, int itProjectId)
    {
        try
        {
            await _storage.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(exception, "IT project {ItProjectId} attachment file {StorageKey} could not be removed.", itProjectId, storageKey);
        }
    }

    private static Dictionary<string, string?> Data(ItProjectAttachment attachment) => new()
    {
        ["ItProjectId"] = attachment.ItProjectId.ToString(CultureInfo.InvariantCulture),
        ["AttachmentId"] = attachment.Id.ToString("D"),
        ["FileName"] = attachment.OriginalFileName,
        ["ContentType"] = attachment.ContentType,
        ["SizeBytes"] = attachment.SizeBytes.ToString(CultureInfo.InvariantCulture)
    };

    private static void EnsureActor(ItProjectActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(actor.UserId))
        {
            throw new InvalidOperationException("The acting user is required.");
        }
    }

    private DateTime UtcNow() => _clock.UtcNow.UtcDateTime;
}
