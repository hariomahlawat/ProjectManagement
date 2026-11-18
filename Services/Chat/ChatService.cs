using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Contracts.Chat;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Chat;

namespace ProjectManagement.Services.Chat;

public enum ChatSendStatus
{
    Sent,
    Invalid,
    RecipientUnavailable,
}

public sealed record ChatSendResult(ChatSendStatus Status, ChatMessageDto? Message, string? Error)
{
    public static ChatSendResult Invalid(string error) => new(ChatSendStatus.Invalid, null, error);
}

public enum ChatUnsendStatus
{
    Unsent,
    NotFound,
    TooLate,
}

/// <summary>
/// Message is set for Unsent (also when it was already unsent: the call is idempotent).
/// NotifyUserIds: everyone in the conversation (both people, or the whole group).
/// </summary>
public sealed record ChatUnsendResult(ChatUnsendStatus Status, ChatMessageDto? Message, IReadOnlyList<string>? NotifyUserIds = null);

public enum ChatChangeStatus
{
    Done,
    NotFound,
    TooLate,
    Invalid,
}

/// <summary>An edit: the message as it now is, and everyone who can see it.</summary>
public sealed record ChatEditResult(ChatChangeStatus Status, ChatMessageDto? Message, IReadOnlyList<string> NotifyUserIds, string? Error = null)
{
    public static ChatEditResult Fail(ChatChangeStatus status, string? error = null) => new(status, null, Array.Empty<string>(), error);
}

/// <summary>A reaction turned on or off: the message's reactions now, and everyone who can see it.</summary>
public sealed record ChatReactResult(ChatChangeStatus Status, ChatReactionsDto? Reactions, IReadOnlyList<string> NotifyUserIds, string? Error = null)
{
    public static ChatReactResult Fail(ChatChangeStatus status, string? error = null) => new(status, null, Array.Empty<string>(), error);
}

/// <summary>
/// Direct person-to-person chat. Everyone may chat with every active (human) user. Messages are
/// kept indefinitely and are only ever returned to the two people in the conversation; there is
/// deliberately no administrator read path.
/// </summary>
public sealed class ChatService
{
    public const int MaxHistoryPage = 100;

    public const string FormerUserName = "Former user";

    public const int MinSearchLength = 2;

    public const int MaxSearchResults = 50;

    public const int MaxUnsentSync = 500;

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ChatPresenceTracker _presence;
    private readonly IOptionsMonitor<ChatOptions> _options;

    public ChatService(ApplicationDbContext db, IClock clock, ChatPresenceTracker presence, IOptionsMonitor<ChatOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _presence = presence ?? throw new ArgumentNullException(nameof(presence));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public TimeSpan UnsendWindow => TimeSpan.FromMinutes(Math.Max(0, _options.CurrentValue.UnsendWindowMinutes));

    public TimeSpan EditWindow => TimeSpan.FromMinutes(Math.Max(0, _options.CurrentValue.EditWindowMinutes));

    internal const string ReplyGoneError = "The message you replied to is not in this conversation any more. Send it without the reply.";

    /// <summary>The most people one message may mention (a group's size).</summary>
    public const int MaxMentions = ChatConversation.MaxGroupMembers;

    /// <summary>
    /// The buddy list: every active human user, plus anyone the caller has a conversation with
    /// even if that account has since been deactivated (shown inactive so history stays readable).
    /// </summary>
    public async Task<IReadOnlyList<ChatContactDto>> ListContactsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var conversations = await (
                from mine in _db.ChatParticipants.AsNoTracking()
                where mine.UserId == userId && mine.Conversation!.Kind == ChatConversation.DirectKind
                join peer in _db.ChatParticipants.AsNoTracking()
                    on mine.ConversationId equals peer.ConversationId
                where peer.UserId != userId
                select new
                {
                    PeerUserId = peer.UserId,
                    mine.Conversation!.LastMessageUtc,
                    Unread = _db.ChatMessages.Count(m =>
                        m.ConversationId == mine.ConversationId
                        && m.SenderUserId != userId
                        && m.UnsentUtc == null
                        && m.Id > (mine.LastReadMessageId ?? 0)),
                })
            .ToListAsync(cancellationToken);

        var byPeer = conversations.ToDictionary(c => c.PeerUserId, StringComparer.Ordinal);
        var peerIds = byPeer.Keys.ToList();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id != userId)
            .Where(u => (u.AccountKind == UserAccountKind.Human && !u.IsDisabled && !u.PendingDeletion)
                || peerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName, u.Rank, u.AccountKind, u.IsDisabled, u.PendingDeletion })
            .ToListAsync(cancellationToken);

        var presence = _presence.Snapshot();
        var listedIds = users.Select(u => u.Id).Concat(peerIds).Distinct().ToList();
        var statuses = await _db.ChatUserStatuses
            .AsNoTracking()
            .Where(s => listedIds.Contains(s.UserId))
            .ToDictionaryAsync(s => s.UserId, s => s.Message, StringComparer.Ordinal, cancellationToken);

        // A peer whose account row has been purged keeps their conversation (no user foreign
        // keys), so they stay listed as an inactive placeholder and the history stays reachable.
        var found = users.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var purged = byPeer.Values
            .Where(c => !found.Contains(c.PeerUserId))
            .Select(c => new ChatContactDto(
                c.PeerUserId,
                FormerUserName,
                null,
                "?",
                IsActive: false,
                IsOnline: false,
                UnreadCount: c.Unread,
                LastMessageUtc: c.LastMessageUtc));

        return users
            .Select(u =>
            {
                var display = ResolveDisplayName(u.FullName, u.UserName, u.Id);
                byPeer.TryGetValue(u.Id, out var conversation);
                var status = presence.GetValueOrDefault(u.Id, ChatPresence.Offline);
                return new ChatContactDto(
                    u.Id,
                    display,
                    string.IsNullOrWhiteSpace(u.Rank) ? null : u.Rank.Trim(),
                    BuildInitials(display),
                    IsActive: IsReachable(u.AccountKind, u.IsDisabled, u.PendingDeletion),
                    IsOnline: status != ChatPresence.Offline,
                    UnreadCount: conversation?.Unread ?? 0,
                    LastMessageUtc: conversation?.LastMessageUtc,
                    IsAway: status == ChatPresence.Away,
                    StatusMessage: statuses.GetValueOrDefault(u.Id));
            })
            .Concat(purged)
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.UserId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// A page of the conversation with <paramref name="peerUserId"/>, oldest first.
    /// With neither cursor: the latest messages. <paramref name="beforeMessageId"/>: the page
    /// that ends just before that id (pass the smallest id shown to load earlier messages, or a
    /// search hit's id + 1 to open the conversation at that message). <paramref name="afterMessageId"/>:
    /// the page that starts just after that id, for loading newer messages below an older page.
    /// </summary>
    public async Task<ChatHistoryDto> GetHistoryAsync(
        string userId,
        string peerUserId,
        long? beforeMessageId,
        int limit,
        CancellationToken cancellationToken = default,
        long? afterMessageId = null)
    {
        var size = Math.Clamp(limit, 1, MaxHistoryPage);
        var key = ChatConversation.BuildDirectKey(userId, peerUserId);

        // Looking the conversation up by the caller's own key is the access check: a user can
        // only ever reach the thread that has them as one of its two members.
        var conversationId = await _db.ChatConversations
            .AsNoTracking()
            .Where(c => c.DirectKey == key)
            .Select(c => (long?)c.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (conversationId is null)
        {
            return new ChatHistoryDto(peerUserId, Array.Empty<ChatMessageDto>(), false);
        }

        var all = _db.ChatMessages.AsNoTracking().Where(m => m.ConversationId == conversationId.Value);
        var (page, hasMore, hasNewer) = await PageAsync(all, beforeMessageId, afterMessageId, size, cancellationToken);

        var peerLastRead = await _db.ChatParticipants
            .AsNoTracking()
            .Where(p => p.ConversationId == conversationId.Value && p.UserId == peerUserId)
            .Select(p => p.LastReadMessageId)
            .SingleOrDefaultAsync(cancellationToken);

        var messages = page
            .Select(m => new ChatMessageDto(
                m.Id,
                m.SenderUserId,
                string.Equals(m.SenderUserId, userId, StringComparison.Ordinal) ? peerUserId : userId,
                m.ClientMessageId,
                m.UnsentUtc is null ? m.Body : string.Empty,
                m.SentUtc,
                m.UnsentUtc is not null))
            .ToList();

        return new ChatHistoryDto(peerUserId, await ChatMessageDetails.WithDetailsAsync(_db, messages, cancellationToken), hasMore, hasNewer, peerLastRead);
    }

    /// <summary>
    /// One page of a conversation, oldest first. No cursor: the latest. before: the page ending
    /// just before that id (HasNewer when it stops short of the latest). after: the page starting
    /// just after that id. Shared by direct and group history.
    /// </summary>
    internal static async Task<(List<ChatMessage> Page, bool HasMore, bool HasNewer)> PageAsync(
        IQueryable<ChatMessage> all,
        long? beforeMessageId,
        long? afterMessageId,
        int limit,
        CancellationToken cancellationToken)
    {
        var size = Math.Clamp(limit, 1, MaxHistoryPage);
        if (afterMessageId is > 0)
        {
            var newer = await all
                .Where(m => m.Id > afterMessageId.Value)
                .OrderBy(m => m.Id)
                .Take(size + 1)
                .ToListAsync(cancellationToken);
            // hasMore: the caller already holds the message it paged after.
            return (newer.Take(size).ToList(), true, newer.Count > size);
        }

        var query = beforeMessageId is > 0 ? all.Where(m => m.Id < beforeMessageId.Value) : all;
        var older = await query
            .OrderByDescending(m => m.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken);
        var page = older.Take(size).OrderBy(m => m.Id).ToList();
        var lastId = page.Count > 0 ? page[page.Count - 1].Id : 0;
        var hasNewer = beforeMessageId is > 0
            && page.Count > 0
            && await all.AnyAsync(m => m.Id > lastId, cancellationToken);
        return (page, older.Count > size, hasNewer);
    }

    /// <summary>
    /// Messages containing <paramref name="query"/> (case-insensitive) across the caller's own
    /// conversations only (direct and group), newest first. Group notices are not searched.
    /// </summary>
    /// <param name="includeGroups">
    /// Group hits only for pages that know them: a page loaded before group chats opens every hit
    /// by its peer user id, which a group hit does not have, and would end up switching chat off.
    /// </param>
    public async Task<IReadOnlyList<ChatSearchHitDto>> SearchAsync(
        string userId,
        string? query,
        int limit,
        CancellationToken cancellationToken = default,
        bool includeGroups = false)
    {
        var term = query?.Trim();
        if (string.IsNullOrEmpty(term) || term.Length < MinSearchLength)
        {
            return Array.Empty<ChatSearchHitDto>();
        }

        var needle = term.ToLowerInvariant();
        var size = Math.Clamp(limit, 1, MaxSearchResults);

        var hits = await (
                from mine in _db.ChatParticipants.AsNoTracking()
                where mine.UserId == userId
                join m in _db.ChatMessages.AsNoTracking()
                    on mine.ConversationId equals m.ConversationId
                where m.UnsentUtc == null && m.EventKind == null && m.Body.ToLower().Contains(needle)
                    && (includeGroups || mine.Conversation!.Kind == ChatConversation.DirectKind)
                    // A group member only ever sees messages from when they (last) joined.
                    && (mine.Conversation!.Kind == ChatConversation.DirectKind || m.SentUtc >= mine.JoinedUtc)
                orderby m.Id descending
                select new { m.Id, m.ConversationId, mine.Conversation!.Kind, mine.Conversation.Title, m.SenderUserId, m.Body, m.SentUtc })
            .Take(size)
            .ToListAsync(cancellationToken);

        if (hits.Count == 0)
        {
            return Array.Empty<ChatSearchHitDto>();
        }

        // The other person of each direct conversation among the hits.
        var directIds = hits.Where(h => h.Kind == ChatConversation.DirectKind).Select(h => h.ConversationId).Distinct().ToList();
        var peers = await _db.ChatParticipants
            .AsNoTracking()
            .Where(p => directIds.Contains(p.ConversationId) && p.UserId != userId)
            .Select(p => new { p.ConversationId, p.UserId })
            .ToListAsync(cancellationToken);
        var peerOf = peers
            .GroupBy(p => p.ConversationId)
            .ToDictionary(g => g.Key, g => g.First().UserId);
        var peerIds = peerOf.Values.Distinct().ToList();
        var names = await _db.Users
            .AsNoTracking()
            .Where(u => peerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => ResolveDisplayName(u.FullName, u.UserName, u.Id), cancellationToken);

        return hits
            .Select(h =>
            {
                if (h.Kind == ChatConversation.GroupKind)
                {
                    return new ChatSearchHitDto(h.Id, string.Empty, h.Title ?? string.Empty, h.SenderUserId, h.Body, h.SentUtc, h.ConversationId);
                }

                var peer = peerOf.GetValueOrDefault(h.ConversationId, string.Empty);
                return new ChatSearchHitDto(h.Id, peer, names.GetValueOrDefault(peer, FormerUserName), h.SenderUserId, h.Body, h.SentUtc);
            })
            .ToList();
    }

    public async Task<ChatSendResult> SendAsync(
        string senderUserId,
        ChatSendRequest request,
        CancellationToken cancellationToken = default)
    {
        var recipientUserId = request.RecipientUserId?.Trim();
        if (string.IsNullOrEmpty(recipientUserId))
        {
            return ChatSendResult.Invalid("Choose who to send the message to.");
        }

        if (string.Equals(recipientUserId, senderUserId, StringComparison.Ordinal))
        {
            return ChatSendResult.Invalid("You cannot send a chat message to yourself.");
        }

        if (request.ClientMessageId == Guid.Empty)
        {
            return ChatSendResult.Invalid("The message is missing its identifier. Refresh the page and try again.");
        }

        var body = NormalizeBody(request.Body);
        if (body.Length == 0 && request.AttachmentIds is not { Count: > 0 })
        {
            return ChatSendResult.Invalid("Type a message before sending.");
        }

        if (body.Length > ChatMessage.MaxBodyLength)
        {
            return ChatSendResult.Invalid($"A message can be at most {ChatMessage.MaxBodyLength} characters.");
        }

        // A retry of a message that already reached the server returns the stored copy.
        var existing = await FindExistingAsync(senderUserId, request.ClientMessageId, cancellationToken);
        if (existing is not null)
        {
            return existing.GroupId is null && string.Equals(existing.RecipientUserId, recipientUserId, StringComparison.Ordinal)
                ? new ChatSendResult(ChatSendStatus.Sent, await ChatMessageDetails.WithDetailsAsync(_db, existing, cancellationToken), null)
                : ChatSendResult.Invalid("This message identifier was already used for a different conversation.");
        }

        var recipient = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == recipientUserId)
            .Select(u => new { u.AccountKind, u.IsDisabled, u.PendingDeletion })
            .SingleOrDefaultAsync(cancellationToken);

        if (recipient is null || !IsReachable(recipient.AccountKind, recipient.IsDisabled, recipient.PendingDeletion))
        {
            return new ChatSendResult(
                ChatSendStatus.RecipientUnavailable,
                null,
                "This person's account is inactive, so they cannot receive messages.");
        }

        var conversation = await GetOrCreateDirectConversationAsync(senderUserId, recipientUserId, cancellationToken);
        if (request.ReplyToMessageId is { } replyTo
            && !await CanReplyToAsync(_db, conversation.Id, replyTo, joinedUtc: null, cancellationToken))
        {
            return ChatSendResult.Invalid(ReplyGoneError);
        }

        var now = _clock.UtcNow;
        var message = new ChatMessage
        {
            ConversationId = conversation.Id,
            SenderUserId = senderUserId,
            ClientMessageId = request.ClientMessageId,
            Body = body,
            SentUtc = now,
            ReplyToMessageId = request.ReplyToMessageId,
        };

        _db.ChatMessages.Add(message);
        conversation.LastMessageUtc = now;

        // Files uploaded first go with the message: checked now, taken atomically once the
        // message is saved, all in one transaction (docs/chat-attachments.md).
        var fileIds = ChatAttachmentService.DistinctIds(request.AttachmentIds);
        var fileError = await ChatAttachmentService.CheckAsync(_db, _options.CurrentValue.Attachments, senderUserId, fileIds, cancellationToken);
        if (fileError is not null)
        {
            _db.ChangeTracker.Clear();
            return ChatSendResult.Invalid(fileError);
        }

        await using var transaction = fileIds.Count > 0 && _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two tabs retried the same message at once; the other request stored it.
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _db.ChangeTracker.Clear();
            existing = await FindExistingAsync(senderUserId, request.ClientMessageId, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return new ChatSendResult(ChatSendStatus.Sent, await ChatMessageDetails.WithDetailsAsync(_db, existing, cancellationToken), null);
        }

        fileError = await ChatAttachmentService.ClaimAsync(_db, senderUserId, fileIds, message.Id, cancellationToken);
        if (fileError is not null)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _db.ChangeTracker.Clear();
            return ChatSendResult.Invalid(fileError);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new ChatSendResult(
            ChatSendStatus.Sent,
            await ChatMessageDetails.WithDetailsAsync(
                _db,
                new ChatMessageDto(message.Id, senderUserId, recipientUserId, message.ClientMessageId, message.Body, message.SentUtc),
                cancellationToken),
            null);
    }

    /// <summary>
    /// Moves the caller's read marker in the conversation with <paramref name="peerUserId"/>
    /// forward to <paramref name="upToMessageId"/> (never backwards, never past the newest message).
    /// </summary>
    /// <returns>The stored marker, or null when there is no conversation yet.</returns>
    public async Task<long?> MarkReadAsync(
        string userId,
        string peerUserId,
        long upToMessageId,
        CancellationToken cancellationToken = default)
    {
        var key = ChatConversation.BuildDirectKey(userId, peerUserId);
        var conversationId = await _db.ChatParticipants
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Conversation!.DirectKey == key)
            .Select(p => (long?)p.ConversationId)
            .SingleOrDefaultAsync(cancellationToken);

        if (conversationId is null)
        {
            return null;
        }

        var newest = await _db.ChatMessages
            .Where(m => m.ConversationId == conversationId && m.Id <= upToMessageId)
            .MaxAsync(m => (long?)m.Id, cancellationToken);

        var mine = _db.ChatParticipants.Where(p => p.ConversationId == conversationId && p.UserId == userId);
        if (newest is { } target)
        {
            // One conditional UPDATE, so overlapping requests from several tabs can only ever
            // move the marker forward: the later, lower marker matches no row.
            await mine
                .Where(p => p.LastReadMessageId == null || p.LastReadMessageId < target)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.LastReadMessageId, target), cancellationToken);
        }

        return await mine.Select(p => p.LastReadMessageId).SingleAsync(cancellationToken);
    }

    /// <summary>
    /// Unsends the caller's own message, within <see cref="UnsendWindow"/> of sending it. The text
    /// is erased; the row stays so the conversation shows "message unsent" in its place.
    /// </summary>
    public async Task<ChatUnsendResult> UnsendAsync(string userId, long messageId, CancellationToken cancellationToken = default)
    {
        // Only the caller's own message, in a conversation they are still in; group notices
        // cannot be unsent.
        var message = await _db.ChatMessages
            .Where(m => m.Id == messageId && m.SenderUserId == userId && m.EventKind == null
                && m.Conversation!.Participants.Any(p => p.UserId == userId))
            .Include(m => m.Conversation)
            .SingleOrDefaultAsync(cancellationToken);

        // Someone else's message, or no such message: indistinguishable to the caller.
        if (message is null)
        {
            return new ChatUnsendResult(ChatUnsendStatus.NotFound, null);
        }

        var isGroup = message.Conversation!.Kind == ChatConversation.GroupKind;
        // In a group, only the members who could see the message (joined by the time it was sent).
        var sentUtc = message.SentUtc;
        var members = await _db.ChatParticipants
            .AsNoTracking()
            .Where(p => p.ConversationId == message.ConversationId && (!isGroup || p.JoinedUtc <= sentUtc))
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);
        var recipientUserId = isGroup
            ? string.Empty
            : members.FirstOrDefault(id => !string.Equals(id, userId, StringComparison.Ordinal)) ?? string.Empty;

        if (message.UnsentUtc is null)
        {
            if (_clock.UtcNow - message.SentUtc > UnsendWindow)
            {
                return new ChatUnsendResult(ChatUnsendStatus.TooLate, null);
            }

            await using var transaction = _db.Database.CurrentTransaction is null
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            DateTimeOffset? unsentUtc = _clock.UtcNow;
            message.UnsentUtc = unsentUtc;
            message.Body = string.Empty;
            await _db.SaveChangesAsync(cancellationToken);

            // Its files go with it (docs/chat-attachments.md, D6): the space returns to the sender
            // and the clean-up job deletes them. A file already removed (by its sender, say)
            // becomes final too: restoring it would charge the allowance for a file nobody can
            // open. One conditional update: a restore committing meanwhile is overridden (the
            // update waits for it, then removes the file again), and a later restore finds the
            // file unsent and leaves it.
            await _db.ChatAttachments
                .Where(a => a.MessageId == message.Id && a.PurgedUtc == null)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(a => a.RemovedUtc, a => a.RemovedUtc ?? unsentUtc)
                        .SetProperty(a => a.RemovalReason, ChatAttachmentRemovalReason.Unsent)
                        .SetProperty(a => a.RemovedByUserId, userId),
                    cancellationToken);
            // The text is gone, and with it who it mentioned and how people reacted to it.
            await _db.ChatMessageMentions.Where(m => m.MessageId == message.Id).ExecuteDeleteAsync(cancellationToken);
            await _db.ChatMessageReactions.Where(r => r.MessageId == message.Id).ExecuteDeleteAsync(cancellationToken);
            Forget<ChatMessageMention>(_db, m => m.MessageId == message.Id);
            Forget<ChatMessageReaction>(_db, r => r.MessageId == message.Id);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }

        return new ChatUnsendResult(
            ChatUnsendStatus.Unsent,
            new ChatMessageDto(
                message.Id, userId, recipientUserId, message.ClientMessageId, string.Empty, message.SentUtc,
                IsUnsent: true, GroupId: isGroup ? message.ConversationId : null),
            members);
    }

    /// <summary>
    /// Ids of messages in the caller's own conversations unsent at or after <paramref name="since"/>,
    /// so a page that missed live unsend events (connection down) can blank them. Bounded: unsend is
    /// only possible shortly after sending, and the caller passes when it lost its connection.
    /// </summary>
    /// <summary>
    /// Edits the caller's own message within <see cref="EditWindow"/> of sending it, in a
    /// conversation they are still in. The text is replaced (earlier text is not kept) and the
    /// message says "Edited"; in a group, the mentions are replaced too.
    /// </summary>
    public async Task<ChatEditResult> EditAsync(string userId, long messageId, ChatEditRequest request, CancellationToken cancellationToken = default)
    {
        if (EditWindow <= TimeSpan.Zero)
        {
            return ChatEditResult.Fail(ChatChangeStatus.Invalid, "Messages cannot be edited.");
        }

        // Not tracked: the text is written with one conditional update below, never by saving
        // this copy, which could be stale by then.
        var message = await _db.ChatMessages
            .AsNoTracking()
            .Where(m => m.Id == messageId && m.SenderUserId == userId && m.EventKind == null
                && m.Conversation!.Participants.Any(p => p.UserId == userId))
            .Include(m => m.Conversation)
            .SingleOrDefaultAsync(cancellationToken);
        if (message is null)
        {
            return ChatEditResult.Fail(ChatChangeStatus.NotFound);
        }

        if (message.UnsentUtc is not null)
        {
            return ChatEditResult.Fail(ChatChangeStatus.Invalid, "An unsent message cannot be edited.");
        }

        var now = _clock.UtcNow;
        if (now - message.SentUtc > EditWindow)
        {
            return ChatEditResult.Fail(ChatChangeStatus.TooLate);
        }

        var body = NormalizeBody(request.Body);
        if (body.Length > ChatMessage.MaxBodyLength)
        {
            return ChatEditResult.Fail(ChatChangeStatus.Invalid, $"A message can be at most {ChatMessage.MaxBodyLength} characters.");
        }

        if (body.Length == 0
            && !await _db.ChatAttachments.AnyAsync(a => a.MessageId == message.Id && a.RemovedUtc == null, cancellationToken))
        {
            return ChatEditResult.Fail(ChatChangeStatus.Invalid, "A message needs some text. To take it back, use Unsend.");
        }

        var isGroup = message.Conversation!.Kind == ChatConversation.GroupKind;
        var mentions = isGroup
            ? await ValidMentionsAsync(_db, message.ConversationId, userId, request.MentionedUserIds, cancellationToken, message.SentUtc)
            : Array.Empty<string>();

        await using var transaction = _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        // One conditional UPDATE: an unsend that committed after the checks above (another tab)
        // makes it match nothing, and the edit is refused rather than writing text back into an
        // unsent message. Under PostgreSQL the row lock orders it against a concurrent unsend.
        var changed = !string.Equals(body, message.Body, StringComparison.Ordinal);
        DateTimeOffset? editedUtc = changed ? now : message.EditedUtc;
        var updated = await _db.ChatMessages
            .Where(m => m.Id == message.Id && m.UnsentUtc == null)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(m => m.Body, body)
                    .SetProperty(m => m.EditedUtc, editedUtc),
                cancellationToken);
        if (updated == 0)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return ChatEditResult.Fail(ChatChangeStatus.Invalid, "An unsent message cannot be edited.");
        }

        message.Body = body;
        message.EditedUtc = editedUtc;

        if (isGroup)
        {
            await _db.ChatMessageMentions.Where(m => m.MessageId == message.Id).ExecuteDeleteAsync(cancellationToken);
            Forget<ChatMessageMention>(_db, m => m.MessageId == message.Id);
            AddMentions(_db, message.Id, mentions);
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var audience = await AudienceAsync(_db, message.ConversationId, isGroup, message.SentUtc, cancellationToken);
        var recipient = isGroup ? string.Empty : audience.FirstOrDefault(id => !string.Equals(id, userId, StringComparison.Ordinal)) ?? string.Empty;
        var dto = await ChatMessageDetails.WithDetailsAsync(
            _db,
            new ChatMessageDto(message.Id, userId, recipient, message.ClientMessageId, message.Body, message.SentUtc,
                GroupId: isGroup ? message.ConversationId : null),
            cancellationToken);
        return new ChatEditResult(ChatChangeStatus.Done, dto, audience);
    }

    /// <summary>
    /// Turns one of the caller's reactions to a message on or off. Anyone who can read the
    /// message may react (in a group: members who had joined by the time it was sent); notices
    /// and unsent messages take no reactions. Repeating an on or off changes nothing.
    /// </summary>
    public async Task<ChatReactResult> ReactAsync(string userId, long messageId, string? kind, bool on, CancellationToken cancellationToken = default)
    {
        if (!ChatReactionKind.IsKnown(kind))
        {
            return ChatReactResult.Fail(ChatChangeStatus.Invalid, "That reaction is not offered.");
        }

        var message = await _db.ChatMessages
            .AsNoTracking()
            .Where(m => m.Id == messageId && m.EventKind == null)
            .Select(m => new { m.Id, m.ConversationId, m.SentUtc, m.UnsentUtc, m.Conversation!.Kind })
            .SingleOrDefaultAsync(cancellationToken);
        var isGroup = message?.Kind == ChatConversation.GroupKind;
        if (message is null
            || !await _db.ChatParticipants.AnyAsync(
                p => p.ConversationId == message.ConversationId && p.UserId == userId && (!isGroup || p.JoinedUtc <= message.SentUtc),
                cancellationToken))
        {
            return ChatReactResult.Fail(ChatChangeStatus.NotFound);
        }

        if (message.UnsentUtc is not null)
        {
            return ChatReactResult.Fail(ChatChangeStatus.Invalid, "An unsent message cannot be reacted to.");
        }

        if (on)
        {
            var exists = await _db.ChatMessageReactions.AnyAsync(r => r.MessageId == messageId && r.UserId == userId && r.Kind == kind, cancellationToken);
            if (!exists)
            {
                var reaction = new ChatMessageReaction { MessageId = messageId, UserId = userId, Kind = kind!, CreatedUtc = _clock.UtcNow };
                _db.ChatMessageReactions.Add(reaction);
                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    // The same reaction from another tab at the same moment: it is there either way.
                }
                finally
                {
                    // Not kept in the context: turning it off deletes in the database directly.
                    _db.Entry(reaction).State = EntityState.Detached;
                }
            }
        }
        else
        {
            await _db.ChatMessageReactions
                .Where(r => r.MessageId == messageId && r.UserId == userId && r.Kind == kind)
                .ExecuteDeleteAsync(cancellationToken);
            Forget<ChatMessageReaction>(_db, r => r.MessageId == messageId && r.UserId == userId && r.Kind == kind);
        }

        var reactions = await ChatMessageDetails.ReactionsOfAsync(_db, messageId, cancellationToken);
        var audience = await AudienceAsync(_db, message.ConversationId, isGroup, message.SentUtc, cancellationToken);
        return new ChatReactResult(ChatChangeStatus.Done, new ChatReactionsDto(messageId, isGroup ? message.ConversationId : null, reactions), audience);
    }

    /// <summary>
    /// Whether a new message may reply to <paramref name="replyToMessageId"/>: a message (not a
    /// notice) of the same conversation, sent after <paramref name="joinedUtc"/> when given (a
    /// group member replies only to what they can see).
    /// </summary>
    internal static Task<bool> CanReplyToAsync(ApplicationDbContext db, long conversationId, long replyToMessageId, DateTimeOffset? joinedUtc, CancellationToken cancellationToken)
        => db.ChatMessages.AnyAsync(
            m => m.Id == replyToMessageId && m.ConversationId == conversationId && m.EventKind == null
                && (joinedUtc == null || m.SentUtc >= joinedUtc),
            cancellationToken);

    /// <summary>
    /// The asked-for mentions that are members of the group now and can see the message (joined
    /// by <paramref name="sentUtc"/>, when editing an existing one), the sender left out; others
    /// are dropped.
    /// </summary>
    internal static async Task<IReadOnlyList<string>> ValidMentionsAsync(
        ApplicationDbContext db,
        long conversationId,
        string senderUserId,
        IReadOnlyList<string>? requested,
        CancellationToken cancellationToken,
        DateTimeOffset? sentUtc = null)
    {
        var wanted = (requested ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 450 && !string.Equals(id, senderUserId, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxMentions)
            .ToList();
        if (wanted.Count == 0)
        {
            return Array.Empty<string>();
        }

        return await db.ChatParticipants
            .AsNoTracking()
            .Where(p => p.ConversationId == conversationId && wanted.Contains(p.UserId)
                && (sentUtc == null || p.JoinedUtc <= sentUtc))
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Stops tracking rows a set delete (ExecuteDelete) just removed: the context would otherwise
    /// still hold them, and adding the same key again in this request would fail.
    /// </summary>
    internal static void Forget<T>(ApplicationDbContext db, Func<T, bool> removed)
        where T : class
    {
        foreach (var entry in db.ChangeTracker.Entries<T>().Where(e => removed(e.Entity)).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    internal static void AddMentions(ApplicationDbContext db, long messageId, IReadOnlyList<string> userIds)
    {
        foreach (var id in userIds)
        {
            db.ChatMessageMentions.Add(new ChatMessageMention { MessageId = messageId, UserId = id });
        }
    }

    /// <summary>Who can see a message: both people of a direct chat; a group's members who had joined by the time it was sent.</summary>
    internal static async Task<IReadOnlyList<string>> AudienceAsync(ApplicationDbContext db, long conversationId, bool isGroup, DateTimeOffset sentUtc, CancellationToken cancellationToken)
        => await db.ChatParticipants
            .AsNoTracking()
            .Where(p => p.ConversationId == conversationId && (!isGroup || p.JoinedUtc <= sentUtc))
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);

    public async Task<ChatUnsentPageDto> ListUnsentSinceAsync(
        string userId,
        DateTimeOffset since,
        long? beforeMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var page = await (
                from mine in _db.ChatParticipants.AsNoTracking()
                where mine.UserId == userId
                join m in _db.ChatMessages.AsNoTracking()
                    on mine.ConversationId equals m.ConversationId
                where m.UnsentUtc != null && m.UnsentUtc >= since
                    && (beforeMessageId == null || m.Id < beforeMessageId)
                orderby m.Id descending
                select m.Id)
            .Take(MaxUnsentSync + 1)
            .ToListAsync(cancellationToken);

        // Paged, never silently cut short: the caller keeps asking while HasMore is set.
        return new ChatUnsentPageDto(page.Take(MaxUnsentSync).ToList(), page.Count > MaxUnsentSync);
    }

    public async Task<string?> GetStatusAsync(string userId, CancellationToken cancellationToken = default)
        => await _db.ChatUserStatuses
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => s.Message)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Sets (or, with blank text, clears) the caller's status message. Returns what is stored.</summary>
    public async Task<string?> SetStatusAsync(string userId, string? message, CancellationToken cancellationToken = default)
    {
        var text = NormalizeStatus(message);
        var existing = await _db.ChatUserStatuses.SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        if (text is null)
        {
            if (existing is not null)
            {
                _db.ChatUserStatuses.Remove(existing);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return null;
        }

        if (existing is null)
        {
            _db.ChatUserStatuses.Add(new ChatUserStatus { UserId = userId, Message = text, UpdatedUtc = _clock.UtcNow });
        }
        else
        {
            existing.Message = text;
            existing.UpdatedUtc = _clock.UtcNow;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (existing is null)
        {
            // Two tabs set a first status at once; the other insert won. Apply this one over it.
            _db.ChangeTracker.Clear();
            var row = await _db.ChatUserStatuses.SingleAsync(s => s.UserId == userId, cancellationToken);
            row.Message = text;
            row.UpdatedUtc = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return text;
    }

    /// <summary>One line of plain text, whitespace collapsed, at most 140 characters; blank = none.</summary>
    internal static string? NormalizeStatus(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var collapsed = string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= ChatUserStatus.MaxMessageLength
            ? collapsed
            : collapsed[..ChatUserStatus.MaxMessageLength].TrimEnd();
    }

    internal static string NormalizeBody(string? body)
    {
        if (body is null)
        {
            return string.Empty;
        }

        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized.Trim();
    }

    private async Task<ChatMessageDto?> FindExistingAsync(string senderUserId, Guid clientMessageId, CancellationToken cancellationToken)
    {
        var stored = await _db.ChatMessages
            .AsNoTracking()
            .Where(m => m.SenderUserId == senderUserId && m.ClientMessageId == clientMessageId)
            .Select(m => new
            {
                m.Id,
                m.ClientMessageId,
                m.Body,
                m.SentUtc,
                m.UnsentUtc,
                m.ConversationId,
                m.Conversation!.Kind,
                RecipientUserId = m.Conversation!.Participants
                    .Where(p => p.UserId != senderUserId)
                    .Select(p => p.UserId)
                    .FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            return null;
        }

        var isGroup = stored.Kind == ChatConversation.GroupKind;
        return new ChatMessageDto(
            stored.Id, senderUserId, isGroup ? string.Empty : stored.RecipientUserId ?? string.Empty,
            stored.ClientMessageId, stored.Body, stored.SentUtc, stored.UnsentUtc is not null,
            isGroup ? stored.ConversationId : null);
    }

    private async Task<ChatConversation> GetOrCreateDirectConversationAsync(
        string userA,
        string userB,
        CancellationToken cancellationToken)
    {
        var key = ChatConversation.BuildDirectKey(userA, userB);
        var conversation = await _db.ChatConversations.SingleOrDefaultAsync(c => c.DirectKey == key, cancellationToken);
        if (conversation is not null)
        {
            return conversation;
        }

        var now = _clock.UtcNow;
        conversation = new ChatConversation
        {
            Kind = ChatConversation.DirectKind,
            DirectKey = key,
            CreatedUtc = now,
        };
        conversation.Participants.Add(new ChatParticipant { UserId = userA, JoinedUtc = now });
        conversation.Participants.Add(new ChatParticipant { UserId = userB, JoinedUtc = now });
        _db.ChatConversations.Add(conversation);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return conversation;
        }
        catch (DbUpdateException)
        {
            // Both people opened a first conversation at the same moment; use the one that won.
            _db.ChangeTracker.Clear();
            return await _db.ChatConversations.SingleAsync(c => c.DirectKey == key, cancellationToken);
        }
    }

    internal static bool IsReachable(UserAccountKind kind, bool isDisabled, bool pendingDeletion)
        => kind == UserAccountKind.Human && !isDisabled && !pendingDeletion;

    internal static string ResolveDisplayName(string? fullName, string? userName, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName.Trim();
        }

        return string.IsNullOrWhiteSpace(userName) ? fallback : userName.Trim();
    }

    internal static string BuildInitials(string name)
    {
        var parts = name.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "?";
        }

        if (parts.Length == 1)
        {
            return parts[0].Length >= 2
                ? string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[0][1]))
                : char.ToUpperInvariant(parts[0][0]).ToString();
        }

        return string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[^1][0]));
    }
}
