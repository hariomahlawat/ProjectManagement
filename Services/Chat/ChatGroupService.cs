using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Contracts.Chat;
using ProjectManagement.Data;
using ProjectManagement.Models.Chat;

namespace ProjectManagement.Services.Chat;

public enum ChatGroupStatus
{
    Ok,
    /// <summary>No such group, or the caller is not in it (indistinguishable to the caller).</summary>
    NotFound,
    /// <summary>A member, but only the group's manager may do this.</summary>
    Forbidden,
    Invalid,
}

/// <summary>
/// The outcome of a change to a group. Notify: everyone now in the group (they get the new
/// group state and the notices). Removed: people who just lost access (they get GroupRemoved).
/// </summary>
public sealed record ChatGroupChange(
    ChatGroupStatus Status,
    string? Error,
    ChatGroupDto? Group,
    IReadOnlyList<ChatMessageDto> Events,
    IReadOnlyList<string> NotifyUserIds,
    IReadOnlyList<string> RemovedUserIds)
{
    public static ChatGroupChange Fail(ChatGroupStatus status, string? error = null)
        => new(status, error, null, Array.Empty<ChatMessageDto>(), Array.Empty<string>(), Array.Empty<string>());
}

public sealed record ChatGroupSendResult(ChatSendStatus Status, ChatMessageDto? Message, string? Error, IReadOnlyList<string> MemberUserIds);

/// <summary>
/// Group chats. Anyone may create a group (a name and up to <see cref="ChatConversation.MaxGroupMembers"/>
/// people, all active human accounts). Any member may add people; only the manager (the
/// creator, or the longest-standing member after the manager leaves) may remove people or
/// rename; anyone may leave. Leaving or being removed deletes the membership row, and with it
/// access to the group's history. Every read and write checks membership on the server.
/// </summary>
public sealed class ChatGroupService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public ChatGroupService(ApplicationDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<IReadOnlyList<ChatGroupSummaryDto>> ListGroupsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var rows = await (
                from mine in _db.ChatParticipants.AsNoTracking()
                where mine.UserId == userId && mine.Conversation!.Kind == ChatConversation.GroupKind
                select new
                {
                    mine.ConversationId,
                    mine.Conversation!.Title,
                    mine.Conversation.ManagerUserId,
                    mine.Conversation.LastMessageUtc,
                    MemberCount = _db.ChatParticipants.Count(p => p.ConversationId == mine.ConversationId),
                    Unread = _db.ChatMessages.Count(m =>
                        m.ConversationId == mine.ConversationId
                        && m.SenderUserId != userId
                        && m.UnsentUtc == null
                        && m.EventKind == null
                        && m.Id > (mine.LastReadMessageId ?? 0)),
                })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ChatGroupSummaryDto(r.ConversationId, r.Title ?? string.Empty, r.ManagerUserId ?? string.Empty, r.MemberCount, r.Unread, r.LastMessageUtc))
            .OrderByDescending(g => g.LastMessageUtc)
            .ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <returns>The group, or null when it does not exist or the caller is not a member.</returns>
    public async Task<ChatGroupDto?> GetGroupAsync(string userId, long groupId, CancellationToken cancellationToken = default)
    {
        var group = await FindGroupForMemberAsync(userId, groupId, track: false, cancellationToken);
        return group is null ? null : await BuildGroupDtoAsync(group, cancellationToken);
    }

    public Task<ChatGroupChange> CreateAsync(
        string userId,
        string? title,
        IReadOnlyList<string>? memberUserIds,
        CancellationToken cancellationToken = default)
        => InTransactionAsync(() => CreateCoreAsync(userId, title, memberUserIds, cancellationToken), cancellationToken);

    public Task<ChatGroupChange> AddMembersAsync(string userId, long groupId, IReadOnlyList<string>? userIds, CancellationToken cancellationToken = default)
        => InTransactionAsync(() => AddMembersCoreAsync(userId, groupId, userIds, cancellationToken), cancellationToken);

    public Task<ChatGroupChange> RemoveMemberAsync(string userId, long groupId, string targetUserId, CancellationToken cancellationToken = default)
        => InTransactionAsync(() => RemoveMemberCoreAsync(userId, groupId, targetUserId, cancellationToken), cancellationToken);

    public Task<ChatGroupChange> LeaveAsync(string userId, long groupId, CancellationToken cancellationToken = default)
        => InTransactionAsync(() => LeaveCoreAsync(userId, groupId, cancellationToken), cancellationToken);

    public Task<ChatGroupChange> RenameAsync(string userId, long groupId, string? title, CancellationToken cancellationToken = default)
        => InTransactionAsync(() => RenameCoreAsync(userId, groupId, title, cancellationToken), cancellationToken);

    /// <summary>
    /// A group change touches membership, the group row and its notices; it is committed as a whole
    /// or not at all. A failed result rolls back anything already written.
    /// </summary>
    private async Task<ChatGroupChange> InTransactionAsync(Func<Task<ChatGroupChange>> change, CancellationToken cancellationToken)
    {
        if (_db.Database.CurrentTransaction is not null)
        {
            return await change();
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var result = await change();
        if (result.Status == ChatGroupStatus.Ok)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();
        }

        return result;
    }

    private async Task<ChatGroupChange> CreateCoreAsync(
        string userId,
        string? title,
        IReadOnlyList<string>? memberUserIds,
        CancellationToken cancellationToken)
    {
        var name = NormalizeTitle(title);
        if (name is null)
        {
            return ChatGroupChange.Fail(ChatGroupStatus.Invalid, "Give the group a name.");
        }

        var others = Distinct(memberUserIds).Where(id => !string.Equals(id, userId, StringComparison.Ordinal)).ToList();
        if (others.Count == 0)
        {
            return ChatGroupChange.Fail(ChatGroupStatus.Invalid, "Add at least one other person.");
        }

        if (others.Count + 1 > ChatConversation.MaxGroupMembers)
        {
            return ChatGroupChange.Fail(ChatGroupStatus.Invalid, $"A group can have at most {ChatConversation.MaxGroupMembers} people.");
        }

        var unreachable = await UnreachableAsync(others, cancellationToken);
        if (unreachable.Count > 0)
        {
            return ChatGroupChange.Fail(ChatGroupStatus.Invalid, "Some of the chosen people have inactive accounts and cannot be added.");
        }

        var now = _clock.UtcNow;
        var group = new ChatConversation
        {
            Kind = ChatConversation.GroupKind,
            Title = name,
            ManagerUserId = userId,
            CreatedUtc = now,
            LastMessageUtc = now,
        };
        group.Participants.Add(new ChatParticipant { UserId = userId, JoinedUtc = now });
        foreach (var id in others)
        {
            group.Participants.Add(new ChatParticipant { UserId = id, JoinedUtc = now });
        }

        _db.ChatConversations.Add(group);
        await _db.SaveChangesAsync(cancellationToken);

        var created = await AddEventAsync(group, userId, ChatGroupEvent.Created, name, cancellationToken);
        var members = group.Participants.Select(p => p.UserId).ToList();
        return new ChatGroupChange(ChatGroupStatus.Ok, null, await BuildGroupDtoAsync(group, cancellationToken), new[] { created }, members, Array.Empty<string>());
    }

    private async Task<ChatGroupChange> AddMembersCoreAsync(
        string userId,
        long groupId,
        IReadOnlyList<string>? userIds,
        CancellationToken cancellationToken)
    {
        var group = await FindGroupForMemberAsync(userId, groupId, track: true, cancellationToken);
        if (group is null)
        {
            return ChatGroupChange.Fail(ChatGroupStatus.NotFound);
        }

        var current = await MemberIdsAsync(group.Id, cancellationToken);
        var adding = Distinct(userIds).Where(id => !current.Contains(id)).ToList();
        if (adding.Count == 0)
        {
            return ChatGroupChange.Fail(ChatGroupStatus.Invalid, "Those people are already in the group.");
        }

        if (current.Count + adding.Count > ChatConversation.MaxGroupMembers)
        {
            return ChatGroupChange.Fail(ChatGroupStatus.Invalid, $"A group can have at most {ChatConversation.MaxGroupMembers} people.");
        }

        if ((await UnreachableAsync(adding, cancellationToken)).Count > 0)
        {
            return ChatGroupChange.Fail(ChatGroupStatus.Invalid, "Some of the chosen people have inactive accounts and cannot be added.");
        }

        var now = _clock.UtcNow;
        foreach (var id in adding)
        {
            _db.ChatParticipants.Add(new ChatParticipant { ConversationId = group.Id, UserId = id, JoinedUtc = now });
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Someone else added one of them at the same moment (the transaction rolls back).
            return ChatGroupChange.Fail(ChatGroupStatus.Invalid, "The group changed at the same time. Try again.");
        }

        var events = new List<ChatMessageDto>();
        foreach (var id in adding)
        {
            events.Add(await AddEventAsync(group, userId, ChatGroupEvent.Added, id, cancellationToken));
        }

        var members = current.Concat(adding).ToList();
        return new ChatGroupChange(ChatGroupStatus.Ok, null, await BuildGroupDtoAsync(group, cancellationToken), events, members, Array.Empty<string>());
    }

    private async Task<ChatGroupChange> RemoveMemberCoreAsync(
        string userId,
        long groupId,
        string targetUserId,
        CancellationToken cancellationToken)
    {
        var group = await FindGroupForMemberAsync(userId, groupId, track: true, cancellationToken);
        if (group is null)
        {
            return ChatGroupChange.Fail(ChatGroupStatus.NotFound);
        }

        if (!string.Equals(group.ManagerUserId, userId, StringComparison.Ordinal))
        {
            return ChatGroupChange.Fail(ChatGroupStatus.Forbidden, "Only the group's manager can remove people.");
        }

        if (string.Equals(targetUserId, userId, StringComparison.Ordinal))
        {
            return ChatGroupChange.Fail(ChatGroupStatus.Invalid, "To leave the group, use Leave group.");
        }

        var removed = await _db.ChatParticipants
            .Where(p => p.ConversationId == group.Id && p.UserId == targetUserId)
            .ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
        {
            return ChatGroupChange.Fail(ChatGroupStatus.Invalid, "That person is not in the group.");
        }

        var notice = await AddEventAsync(group, userId, ChatGroupEvent.Removed, targetUserId, cancellationToken);
        var members = (await MemberIdsAsync(group.Id, cancellationToken)).ToList();
        return new ChatGroupChange(ChatGroupStatus.Ok, null, await BuildGroupDtoAsync(group, cancellationToken), new[] { notice }, members, new[] { targetUserId });
    }

    private async Task<ChatGroupChange> LeaveCoreAsync(string userId, long groupId, CancellationToken cancellationToken)
    {
        var group = await FindGroupForMemberAsync(userId, groupId, track: true, cancellationToken);
        if (group is null)
        {
            return ChatGroupChange.Fail(ChatGroupStatus.NotFound);
        }

        await _db.ChatParticipants
            .Where(p => p.ConversationId == group.Id && p.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        if (string.Equals(group.ManagerUserId, userId, StringComparison.Ordinal))
        {
            // Management passes to the longest-standing member (ties broken by id, so it is stable).
            group.ManagerUserId = await _db.ChatParticipants
                .AsNoTracking()
                .Where(p => p.ConversationId == group.Id)
                .OrderBy(p => p.JoinedUtc)
                .ThenBy(p => p.UserId)
                .Select(p => p.UserId)
                .FirstOrDefaultAsync(cancellationToken) ?? userId;
        }

        var notice = await AddEventAsync(group, userId, ChatGroupEvent.Left, userId, cancellationToken);
        var members = (await MemberIdsAsync(group.Id, cancellationToken)).ToList();
        return new ChatGroupChange(ChatGroupStatus.Ok, null, await BuildGroupDtoAsync(group, cancellationToken), new[] { notice }, members, new[] { userId });
    }

    private async Task<ChatGroupChange> RenameCoreAsync(string userId, long groupId, string? title, CancellationToken cancellationToken)
    {
        var group = await FindGroupForMemberAsync(userId, groupId, track: true, cancellationToken);
        if (group is null)
        {
            return ChatGroupChange.Fail(ChatGroupStatus.NotFound);
        }

        if (!string.Equals(group.ManagerUserId, userId, StringComparison.Ordinal))
        {
            return ChatGroupChange.Fail(ChatGroupStatus.Forbidden, "Only the group's manager can rename it.");
        }

        var name = NormalizeTitle(title);
        if (name is null)
        {
            return ChatGroupChange.Fail(ChatGroupStatus.Invalid, "Give the group a name.");
        }

        if (string.Equals(name, group.Title, StringComparison.Ordinal))
        {
            return ChatGroupChange.Fail(ChatGroupStatus.Invalid, "That is already the group's name.");
        }

        group.Title = name;
        var notice = await AddEventAsync(group, userId, ChatGroupEvent.Renamed, name, cancellationToken);
        var members = (await MemberIdsAsync(group.Id, cancellationToken)).ToList();
        return new ChatGroupChange(ChatGroupStatus.Ok, null, await BuildGroupDtoAsync(group, cancellationToken), new[] { notice }, members, Array.Empty<string>());
    }

    /// <returns>Null when the group does not exist or the caller is not a member.</returns>
    public async Task<ChatGroupHistoryDto?> GetHistoryAsync(
        string userId,
        long groupId,
        long? beforeMessageId,
        int limit,
        long? afterMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var group = await FindGroupForMemberAsync(userId, groupId, track: false, cancellationToken);
        if (group is null)
        {
            return null;
        }

        var all = _db.ChatMessages.AsNoTracking().Where(m => m.ConversationId == group.Id);
        var (page, hasMore, hasNewer) = await ChatService.PageAsync(all, beforeMessageId, afterMessageId, limit, cancellationToken);
        var dto = await BuildGroupDtoAsync(group, cancellationToken);
        return new ChatGroupHistoryDto(group.Id, page.Select(m => ToDto(m, group.Id)).ToList(), hasMore, hasNewer, dto.Members);
    }

    public async Task<ChatGroupSendResult> SendAsync(
        string userId,
        long groupId,
        ChatGroupSendRequest request,
        CancellationToken cancellationToken = default)
    {
        static ChatGroupSendResult Invalid(string error) => new(ChatSendStatus.Invalid, null, error, Array.Empty<string>());

        if (request.ClientMessageId == Guid.Empty)
        {
            return Invalid("The message is missing its identifier. Refresh the page and try again.");
        }

        var body = ChatService.NormalizeBody(request.Body);
        if (body.Length == 0)
        {
            return Invalid("Type a message before sending.");
        }

        if (body.Length > ChatMessage.MaxBodyLength)
        {
            return Invalid($"A message can be at most {ChatMessage.MaxBodyLength} characters.");
        }

        var group = await FindGroupForMemberAsync(userId, groupId, track: true, cancellationToken);
        if (group is null)
        {
            return new ChatGroupSendResult(ChatSendStatus.RecipientUnavailable, null, "You are no longer in this group.", Array.Empty<string>());
        }

        var members = (await MemberIdsAsync(group.Id, cancellationToken)).ToList();

        // A retry of a message that already reached the server returns the stored copy.
        var existing = await FindExistingAsync(userId, request.ClientMessageId, cancellationToken);
        if (existing is not null)
        {
            return existing.ConversationId == group.Id
                ? new ChatGroupSendResult(ChatSendStatus.Sent, ToDto(existing, group.Id), null, members)
                : Invalid("This message identifier was already used for a different conversation.");
        }

        var now = _clock.UtcNow;
        var message = new ChatMessage
        {
            ConversationId = group.Id,
            SenderUserId = userId,
            ClientMessageId = request.ClientMessageId,
            Body = body,
            SentUtc = now,
        };
        _db.ChatMessages.Add(message);
        group.LastMessageUtc = now;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two tabs retried the same message at once; the other request stored it.
            _db.ChangeTracker.Clear();
            existing = await FindExistingAsync(userId, request.ClientMessageId, cancellationToken);
            if (existing is null || existing.ConversationId != group.Id)
            {
                throw;
            }

            return new ChatGroupSendResult(ChatSendStatus.Sent, ToDto(existing, group.Id), null, members);
        }

        return new ChatGroupSendResult(ChatSendStatus.Sent, ToDto(message, group.Id), null, members);
    }

    /// <returns>The stored marker, or null when the caller is not in the group.</returns>
    public async Task<(long? Marker, IReadOnlyList<string> MemberUserIds)> MarkReadAsync(
        string userId,
        long groupId,
        long upToMessageId,
        CancellationToken cancellationToken = default)
    {
        var mine = _db.ChatParticipants.Where(p => p.ConversationId == groupId && p.UserId == userId
            && p.Conversation!.Kind == ChatConversation.GroupKind);
        if (!await mine.AnyAsync(cancellationToken))
        {
            return (null, Array.Empty<string>());
        }

        var newest = await _db.ChatMessages
            .Where(m => m.ConversationId == groupId && m.Id <= upToMessageId)
            .MaxAsync(m => (long?)m.Id, cancellationToken);
        if (newest is { } target)
        {
            // Forward-only, in one conditional UPDATE (see ChatService.MarkReadAsync).
            await mine
                .Where(p => p.LastReadMessageId == null || p.LastReadMessageId < target)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.LastReadMessageId, target), cancellationToken);
        }

        var marker = await mine.Select(p => p.LastReadMessageId).SingleAsync(cancellationToken);
        return (marker, (await MemberIdsAsync(groupId, cancellationToken)).ToList());
    }

    /// <returns>The other members, or null when the caller is not in the group (typing signals).</returns>
    public async Task<IReadOnlyList<string>?> OtherMembersAsync(string userId, long groupId, CancellationToken cancellationToken = default)
    {
        var members = await _db.ChatParticipants
            .AsNoTracking()
            .Where(p => p.ConversationId == groupId && p.Conversation!.Kind == ChatConversation.GroupKind)
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);

        return members.Contains(userId, StringComparer.Ordinal)
            ? members.Where(id => !string.Equals(id, userId, StringComparison.Ordinal)).ToList()
            : null;
    }

    internal static ChatMessageDto ToDto(ChatMessage m, long groupId)
        => new(
            m.Id,
            m.SenderUserId,
            string.Empty,
            m.ClientMessageId,
            m.UnsentUtc is null ? m.Body : string.Empty,
            m.SentUtc,
            m.UnsentUtc is not null,
            groupId,
            m.EventKind,
            m.EventData);

    /// <summary>One line, whitespace collapsed, at most <see cref="ChatConversation.MaxTitleLength"/>; blank = none.</summary>
    internal static string? NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var collapsed = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= ChatConversation.MaxTitleLength
            ? collapsed
            : collapsed[..ChatConversation.MaxTitleLength].TrimEnd();
    }

    private static IEnumerable<string> Distinct(IReadOnlyList<string>? ids)
        => (ids ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(id => id.Length <= 450)
            .Distinct(StringComparer.Ordinal);

    private async Task<ChatConversation?> FindGroupForMemberAsync(string userId, long groupId, bool track, CancellationToken cancellationToken)
    {
        var query = _db.ChatConversations.Where(c =>
            c.Id == groupId
            && c.Kind == ChatConversation.GroupKind
            && c.Participants.Any(p => p.UserId == userId));
        return await (track ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<HashSet<string>> MemberIdsAsync(long groupId, CancellationToken cancellationToken)
        => (await _db.ChatParticipants
                .AsNoTracking()
                .Where(p => p.ConversationId == groupId)
                .Select(p => p.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The ids that are not active human accounts (unknown ids included).</summary>
    private async Task<List<string>> UnreachableAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        var reachable = await _db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.AccountKind, u.IsDisabled, u.PendingDeletion })
            .ToListAsync(cancellationToken);
        var ok = reachable
            .Where(u => ChatService.IsReachable(u.AccountKind, u.IsDisabled, u.PendingDeletion))
            .Select(u => u.Id)
            .ToHashSet(StringComparer.Ordinal);
        return ids.Where(id => !ok.Contains(id)).ToList();
    }

    /// <summary>Stores a group notice (and any pending group change) in one save.</summary>
    private async Task<ChatMessageDto> AddEventAsync(ChatConversation group, string actorUserId, string kind, string data, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var notice = new ChatMessage
        {
            ConversationId = group.Id,
            SenderUserId = actorUserId,
            ClientMessageId = Guid.NewGuid(),
            Body = string.Empty,
            SentUtc = now,
            EventKind = kind,
            EventData = data.Length <= ChatGroupEvent.MaxDataLength ? data : data[..ChatGroupEvent.MaxDataLength],
        };
        _db.ChatMessages.Add(notice);
        group.LastMessageUtc = now; // the group is tracked: every change loads it with tracking
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(notice, group.Id);
    }

    private async Task<ChatGroupDto> BuildGroupDtoAsync(ChatConversation group, CancellationToken cancellationToken)
    {
        var members = await _db.ChatParticipants
            .AsNoTracking()
            .Where(p => p.ConversationId == group.Id)
            .OrderBy(p => p.JoinedUtc)
            .ThenBy(p => p.UserId)
            .Select(p => new { p.UserId, p.LastReadMessageId })
            .ToListAsync(cancellationToken);
        var ids = members.Select(m => m.UserId).ToList();
        var users = await _db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName, u.AccountKind, u.IsDisabled, u.PendingDeletion })
            .ToDictionaryAsync(u => u.Id, StringComparer.Ordinal, cancellationToken);

        return new ChatGroupDto(
            group.Id,
            group.Title ?? string.Empty,
            group.ManagerUserId ?? string.Empty,
            members.Select(m =>
            {
                users.TryGetValue(m.UserId, out var u);
                var name = u is null ? ChatService.FormerUserName : ChatService.ResolveDisplayName(u.FullName, u.UserName, u.Id);
                return new ChatGroupMemberDto(
                    m.UserId,
                    name,
                    u is null ? "?" : ChatService.BuildInitials(name),
                    u is not null && ChatService.IsReachable(u.AccountKind, u.IsDisabled, u.PendingDeletion),
                    m.LastReadMessageId);
            }).ToList());
    }

    private Task<ChatMessage?> FindExistingAsync(string senderUserId, Guid clientMessageId, CancellationToken cancellationToken)
        => _db.ChatMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.SenderUserId == senderUserId && m.ClientMessageId == clientMessageId, cancellationToken);
}
