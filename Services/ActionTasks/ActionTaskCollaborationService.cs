using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Security;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskCollaborationService : IActionTaskCollaborationService
{
    private const int MaxAttachmentsPerUpdate = 10;
    private const long MaxFileSizeBytes = 25L * 1024 * 1024;
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "image/jpeg",
        "image/png",
        "text/plain"
    };

    private readonly ApplicationDbContext _context;
    private readonly ActionTaskPermissionService _permission;
    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly IFileSecurityValidator _fileSecurityValidator;
    private readonly IProtectedFileUrlBuilder _urlBuilder;
    private readonly IActionTrackerClock _clock;

    public ActionTaskCollaborationService(ApplicationDbContext context, ActionTaskPermissionService permission, IUploadRootProvider uploadRootProvider, IFileSecurityValidator fileSecurityValidator, IProtectedFileUrlBuilder urlBuilder, IActionTrackerClock clock)
    {
        _context = context;
        _permission = permission;
        _uploadRootProvider = uploadRootProvider;
        _fileSecurityValidator = fileSecurityValidator;
        _urlBuilder = urlBuilder;
        _clock = clock;
    }

    // SECTION: Add update with optional attachments
    public async Task<ActionTaskUpdate> AddUpdateAsync(int taskId, string body, string updateType, string userId, string role, IReadOnlyList<IFormFile> files, CancellationToken cancellationToken = default)
    {
        files ??= Array.Empty<IFormFile>();

        var task = await _context.ActionTasks.FirstOrDefaultAsync(x => x.Id == taskId && !x.IsDeleted, cancellationToken) ?? throw new InvalidOperationException("Task not found.");
        var hasBody = !string.IsNullOrWhiteSpace(body);
        var hasFiles = files is { Count: > 0 } && files.Any(x => x.Length > 0);

        if (string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Closed tasks cannot be updated.");
        }

        if (!_permission.CanAddTaskUpdate(role, userId, task.AssignedToUserId))
        {
            throw new InvalidOperationException("You are not authorized to add updates for this task.");
        }

        if (hasFiles && !_permission.CanUploadTaskAttachment(role, userId, task.AssignedToUserId))
        {
            throw new InvalidOperationException("You are not authorized to add updates for this task.");
        }

        if (!ActionTaskUpdateTypes.All.Contains(updateType, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid update type.");
        }

        if (!hasBody && !hasFiles)
        {
            throw new InvalidOperationException("Update text is required unless at least one attachment is uploaded.");
        }

        if (files.Count > MaxAttachmentsPerUpdate)
        {
            throw new InvalidOperationException($"A maximum of {MaxAttachmentsPerUpdate} files can be attached per update.");
        }

        // SECTION: Validate all uploaded files before persisting update
        ValidateAttachments(files);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var committedFiles = new List<string>();

        try
        {
            var update = new ActionTaskUpdate
            {
                TaskId = taskId,
                CreatedByUserId = userId,
                CreatedAtUtc = _clock.UtcNow,
                UpdateType = ActionTaskUpdateTypes.All.First(x => string.Equals(x, updateType, StringComparison.OrdinalIgnoreCase)),
                Body = hasBody ? body.Trim() : "Supporting file uploaded.",
                IsDeleted = false
            };

            _context.ActionTaskUpdates.Add(update);
            await _context.SaveChangesAsync(cancellationToken);

            foreach (var file in files.Where(x => x.Length > 0))
            {
                var storedPath = await AddAttachmentAsync(taskId, update.Id, userId, file, cancellationToken);
                if (!string.IsNullOrWhiteSpace(storedPath))
                {
                    committedFiles.Add(storedPath);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return update;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            foreach (var filePath in committedFiles)
            {
                SafeDelete(filePath);
            }

            throw;
        }
    }

    // SECTION: Atomic progress update with optional workflow status change
    public async Task<ActionTaskUpdate?> AddUpdateAndMaybeChangeStatusAsync(int taskId, string body, string? newStatus, string userId, string role, IReadOnlyList<IFormFile> files, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        files ??= Array.Empty<IFormFile>();

        // SECTION: Load and validate the full command before any update or attachment is persisted.
        var task = await _context.ActionTasks.FirstOrDefaultAsync(x => x.Id == taskId && !x.IsDeleted, cancellationToken) ?? throw new InvalidOperationException("Task not found.");
        var hasBody = !string.IsNullOrWhiteSpace(body);
        var hasFiles = files is { Count: > 0 } && files.Any(x => x.Length > 0);
        var requestedStatus = string.IsNullOrWhiteSpace(newStatus) ? null : ResolveStatus(newStatus.Trim());
        var hasStatusChange = !string.IsNullOrWhiteSpace(requestedStatus);

        if (!hasBody && !hasFiles && !hasStatusChange)
        {
            throw new InvalidOperationException("Update text is required unless at least one attachment is uploaded.");
        }

        ValidateUpdatePermissions(task, userId, role, hasFiles);
        ValidateAttachments(files);
        ValidateAtomicStatusChange(task, requestedStatus, userId, role, body);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var committedFiles = new List<string>();

        try
        {
            // SECTION: Create the human progress timeline entry and mutate workflow state in the same EF transaction.
            ActionTaskUpdate? update = null;
            if (hasBody || hasFiles)
            {
                update = new ActionTaskUpdate
                {
                    TaskId = taskId,
                    CreatedByUserId = userId,
                    CreatedAtUtc = _clock.UtcNow,
                    UpdateType = ActionTaskUpdateTypes.Progress,
                    Body = hasBody ? body.Trim() : "Supporting file uploaded.",
                    IsDeleted = false
                };
                _context.ActionTaskUpdates.Add(update);
            }

            if (hasStatusChange && !string.Equals(task.Status, requestedStatus, StringComparison.OrdinalIgnoreCase))
            {
                _context.Entry(task).Property(x => x.RowVersion).OriginalValue = rowVersion;
                ApplyStatusChange(task, requestedStatus!, userId, role, body);
            }

            await _context.SaveChangesAsync(cancellationToken);

            // SECTION: Attachment metadata remains inside the same database transaction; physical files are cleaned up on rollback.
            if (update is not null)
            {
                foreach (var file in files.Where(x => x.Length > 0))
                {
                    var storedPath = await AddAttachmentAsync(taskId, update.Id, userId, file, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(storedPath))
                    {
                        committedFiles.Add(storedPath);
                    }
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return update;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            foreach (var filePath in committedFiles)
            {
                SafeDelete(filePath);
            }

            throw new ActionTaskConcurrencyException("This task was updated by another user. Please reload the task details and try again.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            foreach (var filePath in committedFiles)
            {
                SafeDelete(filePath);
            }

            throw;
        }
    }

    // SECTION: Read updates for inspector thread
    public async Task<List<ActionTaskUpdate>> GetUpdatesAsync(int taskId, string userId, string role, CancellationToken cancellationToken = default)
    {
        var task = await _context.ActionTasks.FirstOrDefaultAsync(x => x.Id == taskId && !x.IsDeleted, cancellationToken) ?? throw new InvalidOperationException("Task not found.");
        if (!_permission.CanViewTaskThread(role, userId, task.AssignedToUserId))
        {
            throw new InvalidOperationException("You are not authorized to view updates for this task.");
        }

        return await _context.ActionTaskUpdates
            .AsNoTracking()
            .Where(x => x.TaskId == taskId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<ActionTaskAttachmentMetadata>>> GetAttachmentMetadataByUpdateAsync(int taskId, string userId, string role, CancellationToken cancellationToken = default)
    {
        var task = await _context.ActionTasks.FirstOrDefaultAsync(x => x.Id == taskId && !x.IsDeleted, cancellationToken) ?? throw new InvalidOperationException("Task not found.");
        if (!_permission.CanViewTaskThread(role, userId, task.AssignedToUserId))
        {
            throw new InvalidOperationException("You are not authorized to view attachments for this task.");
        }

        var attachments = await _context.ActionTaskAttachments
            .AsNoTracking()
            .Where(x => x.TaskId == taskId && !x.IsDeleted && x.UpdateId.HasValue)
            .OrderByDescending(x => x.UploadedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        return attachments
            .GroupBy(x => x.UpdateId!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ActionTaskAttachmentMetadata>)group.Select(ToMetadata).ToList());
    }

    private async Task<string?> AddAttachmentAsync(int taskId, int updateId, string userId, IFormFile file, CancellationToken cancellationToken)
    {
        ValidateAttachment(file);
        if (file.Length <= 0)
        {
            return null;
        }

        var sanitizedFileName = Path.GetFileName(file.FileName);
        var storageKey = BuildStorageKey(taskId, sanitizedFileName, _clock.UtcNow);
        var absolutePath = ResolveAbsolutePath(storageKey);
        var tempFile = Path.GetTempFileName();

        try
        {
            await using (var tempOutput = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await file.CopyToAsync(tempOutput, cancellationToken);
                await tempOutput.FlushAsync(cancellationToken);
            }

            await _fileSecurityValidator.IsSafeAsync(tempFile, file.ContentType, cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            File.Move(tempFile, absolutePath, true);
            tempFile = string.Empty;
        }
        catch
        {
            SafeDelete(tempFile);
            SafeDelete(absolutePath);
            throw;
        }

        _context.ActionTaskAttachments.Add(new ActionTaskAttachment
        {
            TaskId = taskId,
            UpdateId = updateId,
            UploadedByUserId = userId,
            UploadedAtUtc = _clock.UtcNow,
            OriginalFileName = sanitizedFileName,
            StorageKey = storageKey,
            ContentType = file.ContentType,
            FileSize = file.Length,
            IsDeleted = false
        });
        await _context.SaveChangesAsync(cancellationToken);
        return absolutePath;
    }

    // SECTION: Atomic command validation helpers
    private void ValidateUpdatePermissions(ActionTaskItem task, string userId, string role, bool hasFiles)
    {
        if (string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Closed tasks cannot be updated.");
        }

        if (!_permission.CanAddTaskUpdate(role, userId, task.AssignedToUserId))
        {
            throw new InvalidOperationException("You are not authorized to add updates for this task.");
        }

        if (hasFiles && !_permission.CanUploadTaskAttachment(role, userId, task.AssignedToUserId))
        {
            throw new InvalidOperationException("You are not authorized to add updates for this task.");
        }
    }

    private void ValidateAtomicStatusChange(ActionTaskItem task, string? requestedStatus, string userId, string role, string? remarks)
    {
        if (string.IsNullOrWhiteSpace(requestedStatus))
        {
            return;
        }

        if (string.Equals(task.Status, requestedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(requestedStatus, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
        {
            ValidateSubmitCommand(task, userId, role, remarks);
            return;
        }

        if (!_permission.CanUpdateTask(role, userId, task.AssignedToUserId))
        {
            throw new InvalidOperationException("You are not authorized to update this task.");
        }

        if (string.Equals(task.Status, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase)
            || string.Equals(requestedStatus, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Backlog items can only be changed through planning actions.");
        }

        if (string.Equals(requestedStatus, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Use the close action to close a task.");
        }

        if (!ActionTaskStatusWorkflow.IsAllowedTransition(task.Status, requestedStatus))
        {
            throw new InvalidOperationException($"Invalid status transition from {task.Status} to {requestedStatus}.");
        }

        ActionTaskStatusWorkflow.ValidateRemarksForStatusTransition(task.Status, requestedStatus, remarks);
    }

    private void ValidateSubmitCommand(ActionTaskItem task, string userId, string role, string? remarks)
    {
        if (!string.Equals(task.AssignedToUserId, userId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only the assigned user can submit this task.");
        }

        if (!_permission.CanSubmit(role))
        {
            throw new InvalidOperationException("You are not authorized to submit this task.");
        }

        if (string.Equals(task.Status, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Backlog items cannot be submitted.");
        }

        if (string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Closed tasks cannot be submitted.");
        }

        if (!ActionTaskStatusWorkflow.CanSubmitFromStatus(task.Status))
        {
            throw new InvalidOperationException("Only assigned, in-progress, or blocked tasks can be submitted.");
        }

        ActionTaskStatusWorkflow.ValidateRemarksForStatusTransition(task.Status, ActionTaskStatuses.Submitted, remarks);
    }

    private void ApplyStatusChange(ActionTaskItem task, string requestedStatus, string userId, string role, string? remarks)
    {
        var oldStatus = task.Status;
        task.Status = requestedStatus;
        if (string.Equals(requestedStatus, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
        {
            task.SubmittedOn = _clock.UtcNow;
        }

        ActionTaskBucketInvariantValidator.ValidateTaskBucketInvariant(task);
        var auditAction = string.Equals(requestedStatus, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)
            ? "Submitted"
            : "StatusUpdated";
        _context.ActionTaskAuditLogs.Add(new ActionTaskAuditLog
        {
            TaskId = task.Id,
            ActionType = auditAction,
            PerformedByUserId = userId,
            PerformedByRole = role,
            PerformedAt = _clock.UtcNow,
            OldValue = oldStatus,
            NewValue = task.Status,
            Remarks = remarks
        });
    }

    private static string ResolveStatus(string status)
    {
        var resolved = ActionTaskStatuses.All.FirstOrDefault(candidate => string.Equals(candidate, status, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(resolved))
        {
            throw new InvalidOperationException("Invalid status transition.");
        }

        return resolved;
    }

    // SECTION: Attachment validation helpers
    private static void ValidateAttachments(IReadOnlyList<IFormFile> files)
    {
        foreach (var file in files)
        {
            ValidateAttachment(file);
        }
    }

    private static void ValidateAttachment(IFormFile file)
    {
        if (file.Length <= 0)
        {
            return;
        }

        if (file.Length > MaxFileSizeBytes)
        {
            throw new InvalidOperationException("File size exceeds the 25 MB limit.");
        }

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            throw new InvalidOperationException("This file type is not allowed.");
        }
    }

    private ActionTaskAttachmentMetadata ToMetadata(ActionTaskAttachment attachment)
    {
        return new ActionTaskAttachmentMetadata(
            attachment.Id,
            attachment.TaskId,
            attachment.UpdateId,
            attachment.OriginalFileName,
            attachment.ContentType,
            attachment.FileSize,
            _urlBuilder.CreateDownloadUrl(attachment.StorageKey, attachment.OriginalFileName, attachment.ContentType),
            _urlBuilder.CreateInlineUrl(attachment.StorageKey, attachment.OriginalFileName, attachment.ContentType),
            attachment.UploadedAtUtc,
            attachment.UploadedByUserId);
    }

    private static string BuildStorageKey(int taskId, string fileName, DateTime utcNow)
    {
        var token = $"{utcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}-{fileName}";
        return Path.Combine("action-tasks", taskId.ToString(CultureInfo.InvariantCulture), token).Replace('\\', '/');
    }

    private string ResolveAbsolutePath(string storageKey)
    {
        var relative = storageKey.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.Combine(_uploadRootProvider.RootPath, relative);
    }

    private static void SafeDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
