using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Contracts.Chat;
using ProjectManagement.Data;
using ProjectManagement.Models.Chat;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Services.Chat;

public enum ChatAttachmentUploadStatus
{
    Uploaded,
    Invalid,
    TooLarge,
    AllowanceFull,
    StorageFull,
}

public sealed record ChatAttachmentUploadResult(ChatAttachmentUploadStatus Status, ChatAttachmentDto? Attachment, string? Error)
{
    public static ChatAttachmentUploadResult Fail(ChatAttachmentUploadStatus status, string error) => new(status, null, error);
}

/// <summary>A file the caller may download: where it is and how to serve it.</summary>
public sealed record ChatAttachmentFile(string AbsolutePath, string ContentType, string FileName, bool IsImage, bool IsThumbnail);

/// <summary>A removed file: who should hear about it.</summary>
public sealed record ChatAttachmentRemoval(ChatAttachmentRemovedDto Removed, IReadOnlyList<string> NotifyUserIds);

/// <summary>Free space where chat files are stored. An interface so tests can pretend the disk is full.</summary>
public interface IChatDiskSpace
{
    /// <returns>Free bytes on the drive holding <paramref name="path"/>, or null when unknown.</returns>
    long? AvailableBytes(string path);
}

public sealed class ChatDiskSpace : IChatDiskSpace
{
    public long? AvailableBytes(string path)
    {
        try
        {
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
            var mount = MountFor(Path.GetFullPath(path), drives.Select(d => d.RootDirectory.FullName), OperatingSystem.IsWindows());
            return mount is null ? null : drives.First(d => d.RootDirectory.FullName == mount).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The mount point holding <paramref name="path"/>: the longest one it lies under. On Linux the
    /// upload folder may be on its own volume (/mnt/prism-data), not on "/".
    /// </summary>
    internal static string? MountFor(string path, IEnumerable<string> mountPoints, bool ignoreCase)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return mountPoints
            .Where(mount => Contains(mount, path, comparison))
            .OrderByDescending(mount => mount.Length)
            .FirstOrDefault();
    }

    private static bool Contains(string mount, string path, StringComparison comparison)
    {
        var root = mount.TrimEnd('/', '\\');
        if (root.Length == 0)
        {
            return path.StartsWith(mount, comparison); // "/" holds everything
        }

        return path.Equals(root, comparison)
            || (path.StartsWith(root, comparison) && path.Length > root.Length && (path[root.Length] == '/' || path[root.Length] == '\\'));
    }
}

/// <summary>
/// Chat attachments (docs/chat-attachments.md). Files are uploaded first, checked (D4), stored
/// under the upload root with random names (D1) and counted against the sender's allowance
/// (D2); they are then sent with a message. Downloads are checked against who may read that
/// message (D6), on every request. Removing a file, or unsending its message, frees the space;
/// the clean-up job deletes the file afterwards.
/// </summary>
public sealed class ChatAttachmentService
{
    public const string StorageFolder = "chat";

    private readonly ApplicationDbContext _db;
    private readonly IUploadRootProvider _root;
    private readonly IUploadPathResolver _paths;
    private readonly IOptionsMonitor<ChatOptions> _options;
    private readonly IClock _clock;
    private readonly IChatDiskSpace _disk;
    private readonly ILogger<ChatAttachmentService> _logger;

    public ChatAttachmentService(
        ApplicationDbContext db,
        IUploadRootProvider root,
        IUploadPathResolver paths,
        IOptionsMonitor<ChatOptions> options,
        IClock clock,
        IChatDiskSpace disk,
        ILogger<ChatAttachmentService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _disk = disk ?? throw new ArgumentNullException(nameof(disk));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private ChatAttachmentOptions Settings => _options.CurrentValue.Attachments;

    // ------------------------------------------------------------------ upload

    public async Task<ChatAttachmentUploadResult> UploadAsync(string userId, Stream content, string? fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var settings = Settings;

        var name = CleanFileName(fileName);
        if (name.Length == 0)
        {
            return ChatAttachmentUploadResult.Fail(ChatAttachmentUploadStatus.Invalid, "The file has no name.");
        }

        var extension = ChatAttachmentInspector.AllowedExtension(name);
        if (extension is null)
        {
            return ChatAttachmentUploadResult.Fail(ChatAttachmentUploadStatus.Invalid, $"Only {ChatAttachmentInspector.AllowedSummary} files can be sent.");
        }

        var free = _disk.AvailableBytes(_root.RootPath);
        if (free is { } bytesFree && bytesFree < settings.MinimumFreeDiskBytes)
        {
            _logger.LogWarning("Chat upload refused: {FreeBytes} bytes free on the upload disk, below the {Floor} floor.", bytesFree, settings.MinimumFreeDiskBytes);
            return ChatAttachmentUploadResult.Fail(ChatAttachmentUploadStatus.StorageFull, "PRISM's file storage is nearly full, so files cannot be sent right now. Please tell the administrator.");
        }

        if (await UsedBytesAsync(userId, cancellationToken) >= settings.AllowanceBytes)
        {
            return AllowanceFull(settings);
        }

        var temporaryFolder = _paths.ToAbsolute($"{StorageFolder}/tmp");
        Directory.CreateDirectory(temporaryFolder);
        var temporary = Path.Combine(temporaryFolder, $"{Guid.NewGuid():N}.part");
        string? storedPath = null;
        string? thumbnailPath = null;
        try
        {
            var (size, sha256) = await CopyBoundedAsync(content, temporary, settings.MaxFileBytes, cancellationToken);
            if (size is null)
            {
                return ChatAttachmentUploadResult.Fail(ChatAttachmentUploadStatus.TooLarge, $"A file can be at most {settings.MaxFileMb} MB.");
            }

            if (size == 0)
            {
                return ChatAttachmentUploadResult.Fail(ChatAttachmentUploadStatus.Invalid, "The file is empty.");
            }

            var inspection = ChatAttachmentInspector.Inspect(temporary, extension);
            if (!inspection.IsAccepted)
            {
                return ChatAttachmentUploadResult.Fail(ChatAttachmentUploadStatus.Invalid, inspection.Error!);
            }

            var now = _clock.UtcNow;
            var id = Guid.NewGuid().ToString("N");
            var folder = $"{StorageFolder}/{now:yyyy}/{now:MM}";
            var storageKey = $"{folder}/{id}{extension}";
            storedPath = _paths.ToAbsolute(storageKey);
            Directory.CreateDirectory(Path.GetDirectoryName(storedPath)!);
            File.Move(temporary, storedPath);

            string? thumbnailKey = null;
            if (inspection.ContentType.StartsWith("image/", StringComparison.Ordinal))
            {
                var thumbnail = ChatAttachmentInspector.CreateThumbnail(storedPath);
                if (thumbnail is null)
                {
                    return ChatAttachmentUploadResult.Fail(ChatAttachmentUploadStatus.Invalid, "The image could not be read. It may be damaged.");
                }

                thumbnailKey = $"{folder}/{id}.thumb.webp";
                thumbnailPath = _paths.ToAbsolute(thumbnailKey);
                await File.WriteAllBytesAsync(thumbnailPath, thumbnail, cancellationToken);
            }

            var attachment = new ChatAttachment
            {
                UploaderUserId = userId,
                StorageKey = storageKey,
                ThumbnailKey = thumbnailKey,
                FileName = name,
                ContentType = inspection.ContentType,
                SizeBytes = size.Value,
                Sha256 = sha256,
                Width = inspection.Width,
                Height = inspection.Height,
                CreatedUtc = now,
            };

            if (!await SaveWithinAllowanceAsync(attachment, settings, cancellationToken))
            {
                return AllowanceFull(settings);
            }

            storedPath = null; // kept
            thumbnailPath = null;
            return new ChatAttachmentUploadResult(ChatAttachmentUploadStatus.Uploaded, ToDto(attachment), null);
        }
        finally
        {
            DeleteQuietly(temporary);
            DeleteQuietly(storedPath);
            DeleteQuietly(thumbnailPath);
        }
    }

    /// <summary>
    /// Stores the row only if the sender's kept files plus this one fit the allowance. One
    /// person's uploads run one at a time here (a PostgreSQL advisory lock per person), so two
    /// uploads at once cannot both slip under the limit.
    /// </summary>
    private async Task<bool> SaveWithinAllowanceAsync(ChatAttachment attachment, ChatAttachmentOptions settings, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        if (_db.Database.IsNpgsql())
        {
            var lockKey = "chat-attachments:" + attachment.UploaderUserId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", cancellationToken);
        }

        var used = await UsedBytesAsync(attachment.UploaderUserId, cancellationToken);
        if (used + attachment.SizeBytes > settings.AllowanceBytes)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        _db.ChatAttachments.Add(attachment);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static ChatAttachmentUploadResult AllowanceFull(ChatAttachmentOptions settings)
        => ChatAttachmentUploadResult.Fail(
            ChatAttachmentUploadStatus.AllowanceFull,
            $"Your files would go over your {FormatSize(settings.AllowanceBytes)} allowance. Remove files from older messages to make room.");

    private static async Task<(long? Size, string Sha256)> CopyBoundedAsync(Stream content, string path, long maxBytes, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                return (null, string.Empty);
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return (total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    /// <summary>The name as shown and offered on download: no folders, no control characters.</summary>
    internal static string CleanFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/').Split('/').Last());
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim().Trim('.');
        if (name.Length > ChatAttachment.MaxFileNameLength)
        {
            var extension = Path.GetExtension(name);
            name = name[..(ChatAttachment.MaxFileNameLength - extension.Length)] + extension;
        }

        return name;
    }

    // ------------------------------------------------------------------ usage

    /// <summary>The allowance in use: the sizes of the person's files that are still kept (sent or not yet sent).</summary>
    public async Task<long> UsedBytesAsync(string userId, CancellationToken cancellationToken = default)
        => await _db.ChatAttachments
            .Where(a => a.UploaderUserId == userId && a.RemovedUtc == null)
            .SumAsync(a => (long?)a.SizeBytes, cancellationToken) ?? 0;

    public async Task<ChatAttachmentUsageDto> GetUsageAsync(string userId, CancellationToken cancellationToken = default)
    {
        var settings = Settings;
        return new ChatAttachmentUsageDto(await UsedBytesAsync(userId, cancellationToken), settings.AllowanceBytes, settings.MaxFileBytes, settings.MaxFilesPerMessage);
    }

    /// <summary>Administrators: sizes only (D8), never file names or conversations.</summary>
    public async Task<ChatAttachmentTotalsDto> GetTotalsAsync(CancellationToken cancellationToken = default)
    {
        var kept = _db.ChatAttachments.AsNoTracking().Where(a => a.RemovedUtc == null);
        var files = await kept.CountAsync(cancellationToken);
        var total = await kept.SumAsync(a => (long?)a.SizeBytes, cancellationToken) ?? 0;
        var largest = await kept
            .GroupBy(a => a.UploaderUserId)
            .Select(g => new { UserId = g.Key, Bytes = g.Sum(a => a.SizeBytes) })
            .OrderByDescending(x => x.Bytes)
            .Take(10)
            .ToListAsync(cancellationToken);
        var ids = largest.Select(x => x.UserId).ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => ChatService.ResolveDisplayName(u.FullName, u.UserName, u.Id), cancellationToken);
        return new ChatAttachmentTotalsDto(
            files,
            total,
            _disk.AvailableBytes(_root.RootPath),
            largest.Select(x => new ChatAttachmentPersonUsageDto(names.GetValueOrDefault(x.UserId, ChatService.FormerUserName), x.Bytes)).ToList());
    }

    // ------------------------------------------------------------------ sending

    internal const string FileGoneError = "One of the files is no longer available. Attach it again.";

    /// <summary>
    /// Before a message with files is saved: the switch, the count, and that each id is the
    /// sender's own upload, not yet sent and not removed. A friendly early answer only; the
    /// files are taken by <see cref="ClaimAsync"/>, which is what guarantees it.
    /// </summary>
    /// <returns>An error for the sender, or null.</returns>
    internal static async Task<string?> CheckAsync(
        ApplicationDbContext db,
        ChatAttachmentOptions settings,
        string senderUserId,
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return null;
        }

        if (!settings.Enabled)
        {
            return "Files cannot be sent at the moment.";
        }

        if (ids.Count > settings.MaxFilesPerMessage)
        {
            return $"A message can carry at most {settings.MaxFilesPerMessage} files.";
        }

        var available = await db.ChatAttachments
            .CountAsync(a => ids.Contains(a.Id) && a.UploaderUserId == senderUserId && a.MessageId == null && a.RemovedUtc == null, cancellationToken);
        return available == ids.Count ? null : FileGoneError;
    }

    /// <summary>
    /// Takes the uploads for a saved message in one conditional UPDATE: only uploads that are
    /// still the sender's, unsent and not removed are linked. A clean-up sweep or a second send
    /// that got there first leaves fewer rows to take, and the caller rolls the message back.
    /// Run inside the message's transaction.
    /// </summary>
    /// <returns>An error for the sender, or null when every file was taken.</returns>
    internal static async Task<string?> ClaimAsync(
        ApplicationDbContext db,
        string senderUserId,
        IReadOnlyList<long> ids,
        long messageId,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return null;
        }

        var claimed = await db.ChatAttachments
            .Where(a => ids.Contains(a.Id) && a.UploaderUserId == senderUserId && a.MessageId == null && a.RemovedUtc == null)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.MessageId, messageId), cancellationToken);

        // The UPDATE bypassed the change tracker: drop any copies it holds, so later reads in
        // this context see the files as sent.
        foreach (var entry in db.ChangeTracker.Entries<ChatAttachment>().Where(e => ids.Contains(e.Entity.Id)).ToList())
        {
            entry.State = EntityState.Detached;
        }

        return claimed == ids.Count ? null : FileGoneError;
    }

    internal static IReadOnlyList<long> DistinctIds(IReadOnlyList<long>? ids)
        => (ids ?? Array.Empty<long>()).Distinct().ToList();

    /// <summary>The messages, each with its files (in upload order).</summary>
    internal static async Task<IReadOnlyList<ChatMessageDto>> WithAttachmentsAsync(
        ApplicationDbContext db,
        IReadOnlyList<ChatMessageDto> messages,
        CancellationToken cancellationToken)
    {
        var ids = messages.Where(m => m.Id > 0 && m.EventKind is null).Select(m => m.Id).ToList();
        if (ids.Count == 0)
        {
            return messages;
        }

        var files = await db.ChatAttachments
            .AsNoTracking()
            .Where(a => a.MessageId != null && ids.Contains(a.MessageId.Value))
            .OrderBy(a => a.Id)
            .ToListAsync(cancellationToken);
        if (files.Count == 0)
        {
            return messages;
        }

        var byMessage = files.GroupBy(a => a.MessageId!.Value).ToDictionary(g => g.Key, g => (IReadOnlyList<ChatAttachmentDto>)g.Select(ToDto).ToList());
        return messages
            .Select(m => byMessage.TryGetValue(m.Id, out var attached) ? m with { Attachments = attached } : m)
            .ToList();
    }

    internal static async Task<ChatMessageDto> WithAttachmentsAsync(ApplicationDbContext db, ChatMessageDto message, CancellationToken cancellationToken)
        => (await WithAttachmentsAsync(db, new[] { message }, cancellationToken))[0];

    internal static ChatAttachmentDto ToDto(ChatAttachment a)
        => a.RemovedUtc is null
            ? new ChatAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.IsImage, a.Width, a.Height)
            : new ChatAttachmentDto(a.Id, string.Empty, string.Empty, 0, false, null, null, IsRemoved: true, RemovalReason: a.RemovalReason);

    // ------------------------------------------------------------------ download

    /// <summary>
    /// The file, or its thumbnail, if <paramref name="userId"/> may have it: its sender (also
    /// before sending), or someone who can read its message (D6). Null otherwise, the same as
    /// for a file that does not exist.
    /// </summary>
    public async Task<ChatAttachmentFile?> OpenAsync(string userId, long attachmentId, bool thumbnail, CancellationToken cancellationToken = default)
    {
        if (!Settings.Enabled)
        {
            return null;
        }

        var found = await _db.ChatAttachments
            .AsNoTracking()
            .Where(a => a.Id == attachmentId && a.RemovedUtc == null)
            .Select(a => new
            {
                Attachment = a,
                Message = a.Message == null ? null : new { a.Message.ConversationId, a.Message.SentUtc, a.Message.UnsentUtc },
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            return null;
        }

        var attachment = found.Attachment;
        // Before it is sent, a file is its sender's alone. Once sent, it follows its message for
        // everyone, its sender included: someone who left a group loses its files with its history.
        var allowed = found.Message is null
            ? attachment.UploaderUserId == userId
            : found.Message.UnsentUtc is null
                && await CanReadAsync(userId, found.Message.ConversationId, found.Message.SentUtc, cancellationToken);
        if (!allowed)
        {
            return null;
        }

        var key = thumbnail ? attachment.ThumbnailKey : attachment.StorageKey;
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        var path = _paths.ToAbsolute(key);
        if (!File.Exists(path))
        {
            _logger.LogWarning("Chat attachment {AttachmentId} is missing its file {StorageKey}.", attachment.Id, key);
            return null;
        }

        return new ChatAttachmentFile(path, thumbnail ? "image/webp" : attachment.ContentType, attachment.FileName, attachment.IsImage, thumbnail);
    }

    /// <summary>A direct chat's two people; a group's members who joined by the time it was sent.</summary>
    private Task<bool> CanReadAsync(string userId, long conversationId, DateTimeOffset sentUtc, CancellationToken cancellationToken)
        => _db.ChatParticipants.AnyAsync(
            p => p.ConversationId == conversationId
                && p.UserId == userId
                && (p.Conversation!.Kind == ChatConversation.DirectKind || p.JoinedUtc <= sentUtc),
            cancellationToken);

    // ------------------------------------------------------------------ remove

    /// <summary>The sender removes a file from a message (or an upload not yet sent); D3.</summary>
    public async Task<ChatAttachmentRemoval?> RemoveAsync(string userId, long attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await _db.ChatAttachments
            .Include(a => a.Message)
            .ThenInclude(m => m!.Conversation)
            .SingleOrDefaultAsync(a => a.Id == attachmentId && a.UploaderUserId == userId && a.RemovedUtc == null, cancellationToken);
        if (attachment is null)
        {
            return null;
        }

        // Claimed with one conditional update: if an administrator or retention removed it
        // meanwhile, theirs stands (and only they can restore it).
        DateTimeOffset? removedUtc = _clock.UtcNow;
        Guid? batchId = Guid.NewGuid();
        var claimed = await _db.ChatAttachments
            .Where(a => a.Id == attachment.Id && a.RemovedUtc == null)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(a => a.RemovedUtc, removedUtc)
                    .SetProperty(a => a.RemovalReason, ChatAttachmentRemovalReason.Sender)
                    .SetProperty(a => a.RemovedByUserId, userId)
                    .SetProperty(a => a.RemovalBatchId, batchId),
                cancellationToken);
        _db.Entry(attachment).State = EntityState.Detached;
        if (claimed == 0)
        {
            return null;
        }


        var message = attachment.Message;
        if (message is null)
        {
            return new ChatAttachmentRemoval(new ChatAttachmentRemovedDto(attachment.Id, 0, null), new[] { userId });
        }

        var isGroup = message.Conversation!.Kind == ChatConversation.GroupKind;
        var notify = await _db.ChatParticipants
            .AsNoTracking()
            .Where(p => p.ConversationId == message.ConversationId && (!isGroup || p.JoinedUtc <= message.SentUtc))
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);
        return new ChatAttachmentRemoval(
            new ChatAttachmentRemovedDto(attachment.Id, message.Id, isGroup ? message.ConversationId : null, ChatAttachmentRemovalReason.Sender),
            notify);
    }

    /// <summary>
    /// Files removed at or after <paramref name="since"/> from messages the caller may read, so a
    /// page that missed live removal events (connection down) can mark them. Paged by attachment
    /// id, newest first; the caller keeps asking while HasMore is set.
    /// </summary>
    public async Task<ChatRemovedFilesPageDto> ListRemovedSinceAsync(
        string userId,
        DateTimeOffset since,
        long? beforeAttachmentId = null,
        CancellationToken cancellationToken = default)
    {
        var page = await (
                from a in _db.ChatAttachments.AsNoTracking()
                where a.MessageId != null && a.RemovedUtc != null && a.RemovedUtc >= since
                    && (beforeAttachmentId == null || a.Id < beforeAttachmentId)
                join m in _db.ChatMessages.AsNoTracking() on a.MessageId equals m.Id
                join p in _db.ChatParticipants.AsNoTracking() on m.ConversationId equals p.ConversationId
                where p.UserId == userId
                    && (p.Conversation!.Kind == ChatConversation.DirectKind || p.JoinedUtc <= m.SentUtc)
                orderby a.Id descending
                select new { a.Id, MessageId = m.Id, m.ConversationId, p.Conversation!.Kind, a.RemovalReason })
            .Take(ChatService.MaxUnsentSync + 1)
            .ToListAsync(cancellationToken);

        var files = page
            .Take(ChatService.MaxUnsentSync)
            .Select(f => new ChatAttachmentRemovedDto(f.Id, f.MessageId, f.Kind == ChatConversation.GroupKind ? f.ConversationId : null, f.RemovalReason))
            .ToList();
        return new ChatRemovedFilesPageDto(files, page.Count > ChatService.MaxUnsentSync);
    }

    /// <summary>The most file ids one state check takes.</summary>
    public const int MaxStateCheck = 500;

    /// <summary>
    /// The current state of the given sent files, those in messages the caller may read: a page
    /// that remembers files as removed asks after an outage, so one restored meanwhile comes back.
    /// </summary>
    public async Task<IReadOnlyList<ChatAttachmentRestoredDto>> ListCurrentAsync(
        string userId,
        IReadOnlyCollection<long> attachmentIds,
        CancellationToken cancellationToken = default)
    {
        var ids = attachmentIds.Distinct().Take(MaxStateCheck).ToList();
        if (ids.Count == 0)
        {
            return Array.Empty<ChatAttachmentRestoredDto>();
        }

        var rows = await (
                from a in _db.ChatAttachments.AsNoTracking()
                where ids.Contains(a.Id) && a.MessageId != null
                join m in _db.ChatMessages.AsNoTracking() on a.MessageId equals m.Id
                join p in _db.ChatParticipants.AsNoTracking() on m.ConversationId equals p.ConversationId
                where p.UserId == userId
                    && (p.Conversation!.Kind == ChatConversation.DirectKind || p.JoinedUtc <= m.SentUtc)
                select new { Attachment = a, MessageId = m.Id, m.ConversationId, p.Conversation!.Kind })
            .ToListAsync(cancellationToken);
        return rows
            .Select(r => new ChatAttachmentRestoredDto(r.MessageId, r.Kind == ChatConversation.GroupKind ? r.ConversationId : null, ToDto(r.Attachment)))
            .ToList();
    }

    // ------------------------------------------------------------------ clean-up

    /// <summary>
    /// Removes uploads never sent within the allowed time, then deletes the files of removed
    /// attachments. Safe to run again at any time: each step only acts on what is still to do.
    /// </summary>
    /// <returns>How many files were deleted from disk.</returns>
    public async Task<int> CleanUpAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var unusedBefore = now.AddMinutes(-Math.Max(1, Settings.UnusedUploadMinutes));
        await _db.ChatAttachments
            .Where(a => a.MessageId == null && a.RemovedUtc == null && a.CreatedUtc < unusedBefore)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(a => a.RemovedUtc, now)
                    .SetProperty(a => a.RemovalReason, ChatAttachmentRemovalReason.Unused),
                cancellationToken);

        // A removal that can still be undone waits out the undo window before its file goes.
        var undoableBefore = now - Settings.UndoWindow;

        var deleted = 0;
        var batch = await _db.ChatAttachments
            .Where(a => a.RemovedUtc != null && a.PurgedUtc == null)
            .Where(a => (a.RemovalReason != ChatAttachmentRemovalReason.Sender
                        && a.RemovalReason != ChatAttachmentRemovalReason.Administrator
                        && a.RemovalReason != ChatAttachmentRemovalReason.Retention)
                    || a.RemovedUtc < undoableBefore)
            .OrderBy(a => a.Id)
            .Take(500)
            .ToListAsync(cancellationToken);
        foreach (var attachment in batch)
        {
            var fileGone = DeleteQuietly(_paths.ToAbsolute(attachment.StorageKey));
            var thumbnailGone = attachment.ThumbnailKey is null || DeleteQuietly(_paths.ToAbsolute(attachment.ThumbnailKey));
            // A file still on disk (held open, access denied) stays due, for the next sweep.
            if (fileGone && thumbnailGone)
            {
                attachment.PurgedUtc = now;
                deleted++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        CleanTemporaryFolder(now);
        return deleted;
    }

    /// <summary>Half-written uploads left by a crash or a dropped connection.</summary>
    private void CleanTemporaryFolder(DateTimeOffset now)
    {
        try
        {
            var folder = _paths.ToAbsolute($"{StorageFolder}/tmp");
            if (!Directory.Exists(folder))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.part"))
            {
                if (File.GetLastWriteTimeUtc(file) < now.UtcDateTime.AddHours(-6))
                {
                    DeleteQuietly(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Chat temporary uploads could not be cleaned up.");
        }
    }

    /// <returns>True when the file is gone (deleted now, or already missing).</returns>
    private bool DeleteQuietly(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return true;
        }

        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Chat attachment file {Path} could not be deleted; the clean-up job will retry.", path);
            return false;
        }
    }

    /// <summary>"512 B", "840 KB", "12.5 MB", "1 GB", "2.4 GB".</summary>
    internal static string FormatSize(long bytes)
    {
        const double Kb = 1024, Mb = Kb * 1024, Gb = Mb * 1024;
        if (bytes < Kb)
        {
            return $"{bytes} B";
        }

        if (bytes < Mb)
        {
            return $"{Math.Max(1, Math.Round(bytes / Kb))} KB";
        }

        if (bytes < Gb)
        {
            var mb = bytes / Mb;
            return mb < 10 ? $"{mb:0.#} MB" : $"{Math.Round(mb)} MB";
        }

        return $"{bytes / Gb:0.#} GB";
    }
}
