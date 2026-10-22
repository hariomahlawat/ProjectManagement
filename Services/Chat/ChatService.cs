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

/// <summary>Message is set for Unsent (also when it was already unsent: the call is idempotent).</summary>
public sealed record ChatUnsendResult(ChatUnsendStatus Status, ChatMessageDto? Message);

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

    /// <summary>
    /// The buddy list: every active human user, plus anyone the caller has a conversation with
    /// even if that account has since been deactivated (shown inactive so history stays readable).
    /// </summary>
    public async Task<IReadOnlyList<ChatContactDto>> ListContactsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var conversations = await (
                from mine in _db.ChatParticipants.AsNoTracking()
                where mine.UserId == userId
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
        bool hasMore;
        bool hasNewer;
        List<ChatMessage> page;

        if (afterMessageId is > 0)
        {
            var newer = await all
                .Where(m => m.Id > afterMessageId.Value)
                .OrderBy(m => m.Id)
                .Take(size + 1)
                .ToListAsync(cancellationToken);
            hasNewer = newer.Count > size;
            page = newer.Take(size).ToList();
            hasMore = true; // the caller already holds the message it paged after
        }
        else
        {
            var query = beforeMessageId is > 0 ? all.Where(m => m.Id < beforeMessageId.Value) : all;
            var older = await query
                .OrderByDescending(m => m.Id)
                .Take(size + 1)
                .ToListAsync(cancellationToken);
            hasMore = older.Count > size;
            page = older.Take(size).OrderBy(m => m.Id).ToList();
            var lastId = page.Count > 0 ? page[page.Count - 1].Id : 0;
            hasNewer = beforeMessageId is > 0
                && page.Count > 0
                && await all.AnyAsync(m => m.Id > lastId, cancellationToken);
        }

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

        return new ChatHistoryDto(peerUserId, messages, hasMore, hasNewer, peerLastRead);
    }

    /// <summary>
    /// Messages containing <paramref name="query"/> (case-insensitive) across the caller's own
    /// conversations only, newest first.
    /// </summary>
    public async Task<IReadOnlyList<ChatSearchHitDto>> SearchAsync(
        string userId,
        string? query,
        int limit,
        CancellationToken cancellationToken = default)
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
                join peer in _db.ChatParticipants.AsNoTracking()
                    on mine.ConversationId equals peer.ConversationId
                where peer.UserId != userId
                join m in _db.ChatMessages.AsNoTracking()
                    on mine.ConversationId equals m.ConversationId
                where m.UnsentUtc == null && m.Body.ToLower().Contains(needle)
                orderby m.Id descending
                select new { m.Id, PeerUserId = peer.UserId, m.SenderUserId, m.Body, m.SentUtc })
            .Take(size)
            .ToListAsync(cancellationToken);

        if (hits.Count == 0)
        {
            return Array.Empty<ChatSearchHitDto>();
        }

        var peerIds = hits.Select(h => h.PeerUserId).Distinct().ToList();
        var names = await _db.Users
            .AsNoTracking()
            .Where(u => peerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => ResolveDisplayName(u.FullName, u.UserName, u.Id), cancellationToken);

        return hits
            .Select(h => new ChatSearchHitDto(
                h.Id,
                h.PeerUserId,
                names.GetValueOrDefault(h.PeerUserId, FormerUserName),
                h.SenderUserId,
                h.Body,
                h.SentUtc))
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
        if (body.Length == 0)
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
            return string.Equals(existing.RecipientUserId, recipientUserId, StringComparison.Ordinal)
                ? new ChatSendResult(ChatSendStatus.Sent, existing, null)
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
        var now = _clock.UtcNow;
        var message = new ChatMessage
        {
            ConversationId = conversation.Id,
            SenderUserId = senderUserId,
            ClientMessageId = request.ClientMessageId,
            Body = body,
            SentUtc = now,
        };

        _db.ChatMessages.Add(message);
        conversation.LastMessageUtc = now;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two tabs retried the same message at once; the other request stored it.
            _db.ChangeTracker.Clear();
            existing = await FindExistingAsync(senderUserId, request.ClientMessageId, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return new ChatSendResult(ChatSendStatus.Sent, existing, null);
        }

        return new ChatSendResult(
            ChatSendStatus.Sent,
            new ChatMessageDto(message.Id, senderUserId, recipientUserId, message.ClientMessageId, message.Body, message.SentUtc),
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
        var message = await _db.ChatMessages
            .Where(m => m.Id == messageId && m.SenderUserId == userId)
            .SingleOrDefaultAsync(cancellationToken);

        // Someone else's message, or no such message: indistinguishable to the caller.
        if (message is null)
        {
            return new ChatUnsendResult(ChatUnsendStatus.NotFound, null);
        }

        var recipientUserId = await _db.ChatParticipants
            .AsNoTracking()
            .Where(p => p.ConversationId == message.ConversationId && p.UserId != userId)
            .Select(p => p.UserId)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        if (message.UnsentUtc is null)
        {
            if (_clock.UtcNow - message.SentUtc > UnsendWindow)
            {
                return new ChatUnsendResult(ChatUnsendStatus.TooLate, null);
            }

            message.UnsentUtc = _clock.UtcNow;
            message.Body = string.Empty;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new ChatUnsendResult(
            ChatUnsendStatus.Unsent,
            new ChatMessageDto(message.Id, userId, recipientUserId, message.ClientMessageId, string.Empty, message.SentUtc, IsUnsent: true));
    }

    /// <summary>
    /// Ids of messages in the caller's own conversations unsent at or after <paramref name="since"/>,
    /// so a page that missed live unsend events (connection down) can blank them. Bounded: unsend is
    /// only possible shortly after sending, and the caller passes when it lost its connection.
    /// </summary>
    public async Task<IReadOnlyList<long>> ListUnsentSinceAsync(
        string userId,
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        return await (
                from mine in _db.ChatParticipants.AsNoTracking()
                where mine.UserId == userId
                join m in _db.ChatMessages.AsNoTracking()
                    on mine.ConversationId equals m.ConversationId
                where m.UnsentUtc != null && m.UnsentUtc >= since
                orderby m.Id descending
                select m.Id)
            .Take(MaxUnsentSync)
            .ToListAsync(cancellationToken);
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
                RecipientUserId = m.Conversation!.Participants
                    .Where(p => p.UserId != senderUserId)
                    .Select(p => p.UserId)
                    .FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return stored is null
            ? null
            : new ChatMessageDto(stored.Id, senderUserId, stored.RecipientUserId ?? string.Empty, stored.ClientMessageId, stored.Body, stored.SentUtc, stored.UnsentUtc is not null);
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

    private static bool IsReachable(UserAccountKind kind, bool isDisabled, bool pendingDeletion)
        => kind == UserAccountKind.Human && !isDisabled && !pendingDeletion;

    private static string ResolveDisplayName(string? fullName, string? userName, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName.Trim();
        }

        return string.IsNullOrWhiteSpace(userName) ? fallback : userName.Trim();
    }

    private static string BuildInitials(string name)
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
