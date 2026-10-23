using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Contracts.Chat;
using ProjectManagement.Data;
using ProjectManagement.Models.Chat;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Notifications;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Chat;

/// <summary>A live update for one file: who should hear about it.</summary>
public sealed record ChatFileChange(long AttachmentId, long MessageId, long? GroupId, IReadOnlyList<string> NotifyUserIds, ChatAttachmentDto? Restored, string? RemovalReason);

/// <summary>The outcome of a removal or restore, and the live updates to push.</summary>
public sealed record ChatStorageResult(ChatStorageChangeDto Change, IReadOnlyList<ChatFileChange> Updates);

/// <summary>
/// Managing chat file storage (docs/chat-attachments.md, owner decisions 2026-10-23):
/// <list type="bullet">
/// <item>each person manages the files they sent ("My files": remove a selection or everything
/// older than an age, restore within the undo window);</item>
/// <item>administrators clean up everyone's files, one person's, or those older than an age,
/// seeing sizes and people only, never file names or conversations;</item>
/// <item>automatic retention removes sent files older than <see cref="ChatAttachmentOptions.RetentionDays"/>.</item>
/// </list>
/// Every removal (except unsending) can be undone within <see cref="ChatAttachmentOptions.UndoHours"/>
/// by whoever made it; the clean-up job deletes files only after that. People are told (a
/// notification, committed with the removal) when an administrator or retention removes their
/// files, and administrator clean-ups are audited with their scope and counts.
/// </summary>
public sealed class ChatStorageService
{
    /// <summary>Live updates are pushed for at most this many files of one clean-up; open chats catch up on reload.</summary>
    public const int MaxLiveUpdates = 500;

    private const string NotificationRoute = "/Chat?view=files";

    private readonly ApplicationDbContext _db;
    private readonly IOptionsMonitor<ChatOptions> _options;
    private readonly IClock _clock;
    private readonly INotificationOutboxWriter _outbox;
    private readonly IAdminAuditService _audit;
    private readonly ILogger<ChatStorageService> _logger;

    public ChatStorageService(
        ApplicationDbContext db,
        IOptionsMonitor<ChatOptions> options,
        IClock clock,
        INotificationOutboxWriter outbox,
        IAdminAuditService audit,
        ILogger<ChatStorageService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private ChatAttachmentOptions Settings => _options.CurrentValue.Attachments;

    /// <summary>Kept, sent files (uploads not yet sent are short-lived and not managed here).</summary>
    private IQueryable<ChatAttachment> KeptSent => _db.ChatAttachments.Where(a => a.MessageId != null && a.RemovedUtc == null);

    // ------------------------------------------------------------------ my files

    /// <summary>The files I sent that are kept, and those removed that I can still restore.</summary>
    public async Task<ChatMyFilesDto> ListMineAsync(string userId, CancellationToken cancellationToken = default)
    {
        var settings = Settings;
        var undoFrom = _clock.UtcNow - settings.UndoWindow;
        var rows = await _db.ChatAttachments
            .AsNoTracking()
            .Where(a => a.UploaderUserId == userId && a.MessageId != null && a.Message!.UnsentUtc == null)
            .Where(a => a.RemovedUtc == null
                || (a.PurgedUtc == null
                    && a.RemovedUtc > undoFrom
                    && (a.RemovalReason == ChatAttachmentRemovalReason.Sender
                        || a.RemovalReason == ChatAttachmentRemovalReason.Administrator
                        || a.RemovalReason == ChatAttachmentRemovalReason.Retention)))
            .OrderByDescending(a => a.Id)
            .Select(a => new
            {
                Attachment = a,
                a.Message!.SentUtc,
                a.Message.ConversationId,
                a.Message.Conversation!.Kind,
                a.Message.Conversation.Title,
            })
            .ToListAsync(cancellationToken);

        // The other person of each direct conversation, for its name.
        var directIds = rows.Where(r => r.Kind == ChatConversation.DirectKind).Select(r => r.ConversationId).Distinct().ToList();
        var peers = await _db.ChatParticipants
            .AsNoTracking()
            .Where(p => directIds.Contains(p.ConversationId) && p.UserId != userId)
            .Select(p => new { p.ConversationId, p.UserId })
            .ToListAsync(cancellationToken);
        var peerOf = peers.GroupBy(p => p.ConversationId).ToDictionary(g => g.Key, g => g.First().UserId);
        var peerIds = peerOf.Values.Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => peerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => ChatService.ResolveDisplayName(u.FullName, u.UserName, u.Id), cancellationToken);

        var files = rows.Select(r =>
        {
            var a = r.Attachment;
            string key;
            string name;
            if (r.Kind == ChatConversation.GroupKind)
            {
                key = $"g:{r.ConversationId}";
                name = r.Title ?? "Group";
            }
            else
            {
                key = peerOf.GetValueOrDefault(r.ConversationId, string.Empty);
                name = names.GetValueOrDefault(key, ChatService.FormerUserName);
            }

            // Only removals I can undo myself carry a restore deadline here: mine, and retention's.
            var restorable = a.RemovedUtc is { } removed && a.RemovalReason != ChatAttachmentRemovalReason.Administrator
                ? removed + settings.UndoWindow
                : (DateTimeOffset?)null;
            return new ChatMyFileDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.IsImage, r.SentUtc, key, name, a.MessageId!.Value, a.RemovedUtc, a.RemovalReason, restorable);
        }).ToList();

        var used = await _db.ChatAttachments
            .Where(a => a.UploaderUserId == userId && a.RemovedUtc == null)
            .SumAsync(a => (long?)a.SizeBytes, cancellationToken) ?? 0;
        return new ChatMyFilesDto(files, used, settings.AllowanceBytes, settings.UndoHours);
    }

    /// <summary>Removes the given files I sent, as one batch (undoable within the window).</summary>
    public async Task<ChatStorageResult> RemoveMineAsync(string userId, IReadOnlyList<long>? ids, CancellationToken cancellationToken = default)
    {
        var wanted = (ids ?? Array.Empty<long>()).Distinct().Take(1000).ToList();
        var query = KeptSent.Where(a => a.UploaderUserId == userId && wanted.Contains(a.Id));
        return await RemoveAsync(query, ChatAttachmentRemovalReason.Sender, userId, notifyOwners: false, audit: null, cancellationToken);
    }

    /// <summary>Removes every file I sent more than <paramref name="days"/> days ago, as one batch.</summary>
    public async Task<ChatStorageResult> RemoveMineOlderThanAsync(string userId, int days, CancellationToken cancellationToken = default)
    {
        if (days < 1)
        {
            return Failed("Choose an age of at least one day.");
        }

        var cutoff = _clock.UtcNow.AddDays(-days);
        var query = KeptSent.Where(a => a.UploaderUserId == userId && a.CreatedUtc < cutoff);
        return await RemoveAsync(query, ChatAttachmentRemovalReason.Sender, userId, notifyOwners: false, audit: null, cancellationToken);
    }

    /// <summary>
    /// Restores files I removed, or that retention removed, while still within the undo window and
    /// within my allowance. Files an administrator removed are restored by an administrator.
    /// </summary>
    public async Task<ChatStorageResult> RestoreMineAsync(string userId, IReadOnlyList<long>? ids, CancellationToken cancellationToken = default)
    {
        var wanted = (ids ?? Array.Empty<long>()).Distinct().Take(1000).ToList();
        var settings = Settings;
        var undoFrom = _clock.UtcNow - settings.UndoWindow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await LockAllowanceAsync(userId, cancellationToken);
        var restorable = Restorable(undoFrom, ChatAttachmentRemovalReason.Sender, ChatAttachmentRemovalReason.Retention)
            .Where(a => a.UploaderUserId == userId && wanted.Contains(a.Id));
        var bytes = await restorable.SumAsync(a => (long?)a.SizeBytes, cancellationToken) ?? 0;
        var used = await _db.ChatAttachments
            .Where(a => a.UploaderUserId == userId && a.RemovedUtc == null)
            .SumAsync(a => (long?)a.SizeBytes, cancellationToken) ?? 0;
        if (bytes > 0 && used + bytes > settings.AllowanceBytes)
        {
            return Failed($"Restoring these files would go over your {ChatAttachmentService.FormatSize(settings.AllowanceBytes)} allowance. Remove other files first.");
        }

        var restoring = await RestoreAsync(restorable, cancellationToken);
        if (restoring.Count == 0)
        {
            return Failed("These files can no longer be restored.");
        }

        await transaction.CommitAsync(cancellationToken);
        return new ChatStorageResult(
            new ChatStorageChangeDto(restoring.Count, restoring.Sum(a => a.SizeBytes), null, null),
            await UpdatesForAsync(restoring, restored: true, cancellationToken));
    }

    // ------------------------------------------------------------------ administrators

    /// <summary>Everyone with kept files, largest first (for choosing a person to clean up).</summary>
    public async Task<IReadOnlyList<ChatStoragePersonDto>> ListPeopleAsync(CancellationToken cancellationToken = default)
    {
        var usage = await KeptSent
            .GroupBy(a => a.UploaderUserId)
            .Select(g => new { UserId = g.Key, Files = g.Count(), Bytes = g.Sum(a => a.SizeBytes) })
            .ToListAsync(cancellationToken);
        var ids = usage.Select(u => u.UserId).ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => ChatService.ResolveDisplayName(u.FullName, u.UserName, u.Id), cancellationToken);
        return usage
            .Select(u => new ChatStoragePersonDto(u.UserId, names.GetValueOrDefault(u.UserId, ChatService.FormerUserName), u.Files, u.Bytes))
            .OrderByDescending(p => p.Bytes)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>What a clean-up would remove: counts and sizes only.</summary>
    public async Task<ChatStoragePreviewDto> PreviewAsync(ChatStorageScope scope, CancellationToken cancellationToken = default)
    {
        var query = InScope(scope);
        return new ChatStoragePreviewDto(
            await query.CountAsync(cancellationToken),
            await query.SumAsync(a => (long?)a.SizeBytes, cancellationToken) ?? 0,
            await query.Select(a => a.UploaderUserId).Distinct().CountAsync(cancellationToken));
    }

    /// <summary>An administrator removes the files in scope, as one batch; audited, owners told.</summary>
    public async Task<ChatStorageResult> RemoveAsAdministratorAsync(string adminUserId, string? adminName, ChatStorageScope scope, CancellationToken cancellationToken = default)
    {
        if (scope.OlderThanDays is < 0)
        {
            return Failed("The age cannot be negative.");
        }

        return await RemoveAsync(
            InScope(scope),
            ChatAttachmentRemovalReason.Administrator,
            adminUserId,
            notifyOwners: true,
            change => _audit.RecordAsync(new AdminAuditEntry(
                "Chat.AttachmentsRemoved",
                "ChatAttachmentBatch",
                change.BatchId?.ToString(),
                After: new { change.Files, change.Bytes, Scope = DescribeScope(scope), change.RestorableUntil },
                ActorUserId: adminUserId,
                ActorName: adminName,
                Origin: "Admin.ChatStorage"), cancellationToken),
            cancellationToken);
    }

    /// <summary>Recent clean-ups by administrators and retention, newest first.</summary>
    public async Task<IReadOnlyList<ChatStorageBatchDto>> ListBatchesAsync(CancellationToken cancellationToken = default)
    {
        var since = _clock.UtcNow.AddDays(-7);
        var undoFrom = _clock.UtcNow - Settings.UndoWindow;
        var batches = await _db.ChatAttachments
            .AsNoTracking()
            .Where(a => a.RemovalBatchId != null && a.RemovedUtc > since)
            .Where(a => a.RemovalReason == ChatAttachmentRemovalReason.Administrator || a.RemovalReason == ChatAttachmentRemovalReason.Retention)
            .GroupBy(a => new { BatchId = a.RemovalBatchId!.Value, Reason = a.RemovalReason! })
            .Select(g => new
            {
                g.Key.BatchId,
                g.Key.Reason,
                RemovedUtc = g.Max(a => a.RemovedUtc!.Value),
                Files = g.Count(),
                Bytes = g.Sum(a => a.SizeBytes),
                People = g.Select(a => a.UploaderUserId).Distinct().Count(),
                Purged = g.Count(a => a.PurgedUtc != null),
            })
            .ToListAsync(cancellationToken);
        return batches
            .OrderByDescending(b => b.RemovedUtc)
            .Select(b => new ChatStorageBatchDto(
                b.BatchId, b.Reason, b.RemovedUtc, b.Files, b.Bytes, b.People,
                CanUndo: b.Purged < b.Files && b.RemovedUtc > undoFrom,
                RestorableUntil: b.RemovedUtc + Settings.UndoWindow))
            .Take(20)
            .ToList();
    }

    /// <summary>
    /// An administrator undoes a clean-up (theirs, another administrator's, or a retention run)
    /// within the window: every file of it not yet deleted from disk is restored. Allowances are
    /// not checked; the files were there before.
    /// </summary>
    public async Task<ChatStorageResult> UndoBatchAsync(string adminUserId, string? adminName, Guid batchId, CancellationToken cancellationToken = default)
    {
        var undoFrom = _clock.UtcNow - Settings.UndoWindow;
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var restoring = await RestoreAsync(
            Restorable(undoFrom, ChatAttachmentRemovalReason.Administrator, ChatAttachmentRemovalReason.Retention)
                .Where(a => a.RemovalBatchId == batchId),
            cancellationToken);
        if (restoring.Count == 0)
        {
            return Failed("This clean-up can no longer be undone.");
        }

        var bytes = restoring.Sum(a => a.SizeBytes);
        // In the same transaction: a restore is never left unaudited.
        await _audit.RecordAsync(new AdminAuditEntry(
            "Chat.AttachmentsRestored",
            "ChatAttachmentBatch",
            batchId.ToString(),
            After: new { Files = restoring.Count, Bytes = bytes },
            ActorUserId: adminUserId,
            ActorName: adminName,
            Origin: "Admin.ChatStorage"), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ChatStorageResult(
            new ChatStorageChangeDto(restoring.Count, bytes, batchId, null),
            await UpdatesForAsync(restoring.Take(MaxLiveUpdates).ToList(), restored: true, cancellationToken));
    }

    // ------------------------------------------------------------------ retention

    /// <summary>
    /// Removes sent files older than the retention age, as one batch (undoable within the
    /// window); owners are told. Safe to run again: only files still kept are taken.
    /// </summary>
    public async Task<ChatStorageResult> ApplyRetentionAsync(CancellationToken cancellationToken = default)
    {
        var days = Settings.RetentionDays;
        if (days <= 0)
        {
            return Failed("Retention is off.");
        }

        var cutoff = _clock.UtcNow.AddDays(-days);
        var result = await RemoveAsync(
            KeptSent.Where(a => a.CreatedUtc < cutoff),
            ChatAttachmentRemovalReason.Retention,
            null,
            notifyOwners: true,
            change => _audit.RecordAsync(new AdminAuditEntry(
                "Chat.AttachmentsRemovedByRetention",
                "ChatAttachmentBatch",
                change.BatchId?.ToString(),
                After: new { change.Files, change.Bytes, OlderThanDays = days, change.RestorableUntil },
                Origin: "Chat.Retention"), cancellationToken),
            cancellationToken);
        if (result.Change.Files > 0)
        {
            _logger.LogInformation("Chat retention removed {Files} file(s), {Bytes} bytes, older than {Days} days.", result.Change.Files, result.Change.Bytes, days);
        }

        return result;
    }

    // ------------------------------------------------------------------ shared

    private IQueryable<ChatAttachment> InScope(ChatStorageScope scope)
    {
        var query = KeptSent;
        if (!string.IsNullOrWhiteSpace(scope.UserId))
        {
            query = query.Where(a => a.UploaderUserId == scope.UserId);
        }

        if (scope.OlderThanDays is > 0)
        {
            var cutoff = _clock.UtcNow.AddDays(-scope.OlderThanDays.Value);
            query = query.Where(a => a.CreatedUtc < cutoff);
        }

        return query;
    }

    private static string DescribeScope(ChatStorageScope scope)
    {
        var who = string.IsNullOrWhiteSpace(scope.UserId) ? "everyone" : $"user {scope.UserId}";
        return scope.OlderThanDays is > 0 ? $"{who}, older than {scope.OlderThanDays} days" : who;
    }

    /// <summary>
    /// Marks the files removed as one batch, queues each owner a notification (administrators and
    /// retention) and records the audit entry, all in one transaction. The files are claimed with
    /// one conditional update, so of two overlapping removals each takes only files still kept
    /// when it runs: neither overwrites the other's reason or batch, and counts and notifications
    /// cover only what this one removed.
    /// </summary>
    private async Task<ChatStorageResult> RemoveAsync(
        IQueryable<ChatAttachment> query,
        string reason,
        string? byUserId,
        bool notifyOwners,
        Func<ChatStorageChangeDto, Task>? audit,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        DateTimeOffset? removedUtc = _clock.UtcNow;
        Guid? batch = Guid.NewGuid();
        var claimed = await query.ExecuteUpdateAsync(
            set => set
                .SetProperty(a => a.RemovedUtc, removedUtc)
                .SetProperty(a => a.RemovalReason, reason)
                .SetProperty(a => a.RemovedByUserId, byUserId)
                .SetProperty(a => a.RemovalBatchId, batch),
            cancellationToken);
        if (claimed == 0)
        {
            return Failed("There are no files to remove.");
        }

        var now = removedUtc.Value;
        var batchId = batch.Value;
        var files = await _db.ChatAttachments.AsNoTracking().Where(a => a.RemovalBatchId == batchId).ToListAsync(cancellationToken);
        var restorableUntil = now + Settings.UndoWindow;
        if (notifyOwners)
        {
            foreach (var owner in files.GroupBy(f => f.UploaderUserId))
            {
                await QueueRemovalNoticeAsync(owner.Key, owner.Count(), owner.Sum(f => f.SizeBytes), reason, byUserId, batchId, restorableUntil, cancellationToken);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        var change = new ChatStorageChangeDto(files.Count, files.Sum(f => f.SizeBytes), batchId, restorableUntil);
        if (audit is not null)
        {
            await audit(change);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ChatStorageResult(
            change,
            await UpdatesForAsync(files.Take(MaxLiveUpdates).ToList(), restored: false, cancellationToken));
    }

    private Task QueueRemovalNoticeAsync(string ownerUserId, int files, long bytes, string reason, string? byUserId, Guid batchId, DateTimeOffset restorableUntil, CancellationToken cancellationToken)
    {
        var until = TimeZoneInfo.ConvertTime(restorableUntil, TimeZoneHelper.GetIst()).ToString("d MMM, HH:mm", CultureInfo.InvariantCulture);
        var count = files == 1 ? "1 of your chat files" : $"{files} of your chat files";
        var size = ChatAttachmentService.FormatSize(bytes);
        var (title, summary) = reason == ChatAttachmentRemovalReason.Retention
            ? ("Old chat files removed",
                $"{count} ({size}) were removed automatically because they are more than {Settings.RetentionDays} days old. You can restore them from My files until {until}.")
            : ("Chat files removed by an administrator",
                $"An administrator removed {count} ({size}) to free storage. An administrator can restore them until {until}.");
        return _outbox.QueueAsync(
            NotificationKind.ChatFilesRemoved,
            new[] { ownerUserId },
            new { batchId, files, bytes, reason },
            module: "Chat",
            eventType: "ChatFilesRemoved",
            scopeType: "ChatAttachmentBatch",
            scopeId: batchId.ToString(),
            projectId: null,
            actorUserId: byUserId,
            route: NotificationRoute,
            title: title,
            summary: summary,
            fingerprint: $"chat-files-removed:{batchId:N}:{ownerUserId}",
            cancellationToken);
    }

    /// <summary>Removed sent files, still on disk and within the undo window, removed for one of the reasons; never those of an unsent message.</summary>
    private IQueryable<ChatAttachment> Restorable(DateTimeOffset undoFrom, string reason, string otherReason)
        => _db.ChatAttachments.Where(a => a.MessageId != null
            && a.RemovedUtc != null && a.RemovedUtc > undoFrom && a.PurgedUtc == null
            && (a.RemovalReason == reason || a.RemovalReason == otherReason)
            && a.Message!.UnsentUtc == null);

    /// <summary>
    /// Restores the files the query matches when the update runs (one conditional update, so a
    /// file another removal or restore changed meanwhile is left alone) and returns them.
    /// </summary>
    private async Task<IReadOnlyList<ChatAttachment>> RestoreAsync(IQueryable<ChatAttachment> query, CancellationToken cancellationToken)
    {
        var ids = await query.Select(a => a.Id).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return Array.Empty<ChatAttachment>();
        }

        DateTimeOffset? none = null;
        Guid? noBatch = null;
        await query.Where(a => ids.Contains(a.Id)).ExecuteUpdateAsync(
            set => set
                .SetProperty(a => a.RemovedUtc, none)
                .SetProperty(a => a.RemovalReason, (string?)null)
                .SetProperty(a => a.RemovedByUserId, (string?)null)
                .SetProperty(a => a.RemovalBatchId, noBatch),
            cancellationToken);
        return await _db.ChatAttachments.AsNoTracking()
            .Where(a => ids.Contains(a.Id) && a.RemovedUtc == null)
            .ToListAsync(cancellationToken);
    }

    /// <summary>For each file, who can see its message: they get the live update.</summary>
    private async Task<IReadOnlyList<ChatFileChange>> UpdatesForAsync(IReadOnlyList<ChatAttachment> files, bool restored, CancellationToken cancellationToken)
    {
        var messageIds = files.Select(f => f.MessageId!.Value).Distinct().ToList();
        var messages = await _db.ChatMessages
            .AsNoTracking()
            .Where(m => messageIds.Contains(m.Id))
            .Select(m => new { m.Id, m.ConversationId, m.SentUtc, IsGroup = m.Conversation!.Kind == ChatConversation.GroupKind })
            .ToListAsync(cancellationToken);
        var conversationIds = messages.Select(m => m.ConversationId).Distinct().ToList();
        var participants = await _db.ChatParticipants
            .AsNoTracking()
            .Where(p => conversationIds.Contains(p.ConversationId))
            .Select(p => new { p.ConversationId, p.UserId, p.JoinedUtc })
            .ToListAsync(cancellationToken);

        var byMessage = messages.ToDictionary(m => m.Id);
        return files.Select(file =>
        {
            var message = byMessage[file.MessageId!.Value];
            var notify = participants
                .Where(p => p.ConversationId == message.ConversationId && (!message.IsGroup || p.JoinedUtc <= message.SentUtc))
                .Select(p => p.UserId)
                .ToList();
            return new ChatFileChange(
                file.Id,
                message.Id,
                message.IsGroup ? message.ConversationId : null,
                notify,
                restored ? ChatAttachmentService.ToDto(file) : null,
                restored ? null : file.RemovalReason);
        }).ToList();
    }

    private async Task LockAllowanceAsync(string userId, CancellationToken cancellationToken)
    {
        if (_db.Database.IsNpgsql())
        {
            var lockKey = "chat-attachments:" + userId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", cancellationToken);
        }
    }

    private static ChatStorageResult Failed(string error)
        => new(new ChatStorageChangeDto(0, 0, null, null, error), Array.Empty<ChatFileChange>());
}
