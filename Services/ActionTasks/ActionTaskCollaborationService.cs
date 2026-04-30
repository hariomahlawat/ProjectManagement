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

    public ActionTaskCollaborationService(ApplicationDbContext context, ActionTaskPermissionService permission, IUploadRootProvider uploadRootProvider, IFileSecurityValidator fileSecurityValidator, IProtectedFileUrlBuilder urlBuilder)
    {
        _context = context;
        _permission = permission;
        _uploadRootProvider = uploadRootProvider;
        _fileSecurityValidator = fileSecurityValidator;
        _urlBuilder = urlBuilder;
    }

    // SECTION: Add update with optional attachments
    public async Task<ActionTaskUpdate> AddUpdateAsync(int taskId, string body, string updateType, string userId, string role, IReadOnlyList<IFormFile> files, CancellationToken cancellationToken = default)
    {
        files ??= Array.Empty<IFormFile>();

        var task = await _context.ActionTasks.FirstOrDefaultAsync(x => x.Id == taskId && !x.IsDeleted, cancellationToken) ?? throw new InvalidOperationException("Task not found.");
        if (!_permission.CanViewLogs(role, userId, task.AssignedToUserId))
        {
            throw new InvalidOperationException("You are not authorized to add updates for this task.");
        }

        if (!ActionTaskUpdateTypes.All.Contains(updateType, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid update type.");
        }

        var hasBody = !string.IsNullOrWhiteSpace(body);
        var hasFiles = files is { Count: > 0 } && files.Any(x => x.Length > 0);
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

        var update = new ActionTaskUpdate
        {
            TaskId = taskId,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdateType = ActionTaskUpdateTypes.All.First(x => string.Equals(x, updateType, StringComparison.OrdinalIgnoreCase)),
            Body = hasBody ? body.Trim() : "Attachment update",
            IsDeleted = false
        };
        _context.ActionTaskUpdates.Add(update);
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var file in files.Where(x => x.Length > 0))
        {
            await AddAttachmentAsync(taskId, update.Id, userId, file, cancellationToken);
        }

        return update;
    }

    // SECTION: Read updates for inspector thread
    public async Task<List<ActionTaskUpdate>> GetUpdatesAsync(int taskId, string userId, string role, CancellationToken cancellationToken = default)
    {
        var task = await _context.ActionTasks.FirstOrDefaultAsync(x => x.Id == taskId && !x.IsDeleted, cancellationToken) ?? throw new InvalidOperationException("Task not found.");
        if (!_permission.CanViewLogs(role, userId, task.AssignedToUserId))
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
        if (!_permission.CanViewLogs(role, userId, task.AssignedToUserId))
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

    private async Task AddAttachmentAsync(int taskId, int updateId, string userId, IFormFile file, CancellationToken cancellationToken)
    {
        ValidateAttachment(file);
        if (file.Length <= 0)
        {
            return;
        }

        var sanitizedFileName = Path.GetFileName(file.FileName);
        var storageKey = BuildStorageKey(taskId, sanitizedFileName);
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
            UploadedAtUtc = DateTime.UtcNow,
            OriginalFileName = sanitizedFileName,
            StorageKey = storageKey,
            ContentType = file.ContentType,
            FileSize = file.Length,
            IsDeleted = false
        });
        await _context.SaveChangesAsync(cancellationToken);
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

    private static string BuildStorageKey(int taskId, string fileName)
    {
        var token = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}-{fileName}";
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
