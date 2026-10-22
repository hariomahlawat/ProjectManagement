using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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

/// <summary>
/// Direct person-to-person chat. Everyone may chat with every active (human) user. Messages are
/// kept indefinitely and are only ever returned to the two people in the conversation; there is
/// deliberately no administrator read path.
/// </summary>
public sealed class ChatService
{
    public const int MaxHistoryPage = 100;

    public const string FormerUserName = "Former user";

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ChatPresenceTracker _presence;

    public ChatService(ApplicationDbContext db, IClock clock, ChatPresenceTracker presence)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _presence = presence ?? throw new ArgumentNullException(nameof(presence));
    }

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

        var online = _presence.OnlineUserIds();

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
                return new ChatContactDto(
                    u.Id,
                    display,
                    string.IsNullOrWhiteSpace(u.Rank) ? null : u.Rank.Trim(),
                    BuildInitials(display),
                    IsActive: IsReachable(u.AccountKind, u.IsDisabled, u.PendingDeletion),
                    IsOnline: online.Contains(u.Id),
                    UnreadCount: conversation?.Unread ?? 0,
                    LastMessageUtc: conversation?.LastMessageUtc);
            })
            .Concat(purged)
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.UserId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// A page of the conversation with <paramref name="peerUserId"/>, oldest first. Pass the
    /// smallest id already shown as <paramref name="beforeMessageId"/> to load earlier messages.
    /// </summary>
    public async Task<ChatHistoryDto> GetHistoryAsync(
        string userId,
        string peerUserId,
        long? beforeMessageId,
        int limit,
        CancellationToken cancellationToken = default)
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

        var query = _db.ChatMessages.AsNoTracking().Where(m => m.ConversationId == conversationId.Value);
        if (beforeMessageId is > 0)
        {
            query = query.Where(m => m.Id < beforeMessageId.Value);
        }

        var page = await query
            .OrderByDescending(m => m.Id)
            .Take(size + 1)
            .Select(m => new { m.Id, m.SenderUserId, m.ClientMessageId, m.Body, m.SentUtc })
            .ToListAsync(cancellationToken);

        var messages = page
            .Take(size)
            .OrderBy(m => m.Id)
            .Select(m => new ChatMessageDto(
                m.Id,
                m.SenderUserId,
                string.Equals(m.SenderUserId, userId, StringComparison.Ordinal) ? peerUserId : userId,
                m.ClientMessageId,
                m.Body,
                m.SentUtc))
            .ToList();

        return new ChatHistoryDto(peerUserId, messages, page.Count > size);
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
                RecipientUserId = m.Conversation!.Participants
                    .Where(p => p.UserId != senderUserId)
                    .Select(p => p.UserId)
                    .FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return stored is null
            ? null
            : new ChatMessageDto(stored.Id, senderUserId, stored.RecipientUserId ?? string.Empty, stored.ClientMessageId, stored.Body, stored.SentUtc);
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
