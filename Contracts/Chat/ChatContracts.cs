using System;
using System.Collections.Generic;

namespace ProjectManagement.Contracts.Chat;

public sealed record ChatContactDto(
    string UserId,
    string DisplayName,
    string? Rank,
    string Initials,
    bool IsActive,
    bool IsOnline,
    int UnreadCount,
    DateTimeOffset? LastMessageUtc);

/// <summary>A direct message. Each side works out the other person from the two user ids.</summary>
public sealed record ChatMessageDto(
    long Id,
    string SenderUserId,
    string RecipientUserId,
    Guid ClientMessageId,
    string Body,
    DateTimeOffset SentUtc);

public sealed record ChatHistoryDto(
    string PeerUserId,
    IReadOnlyList<ChatMessageDto> Messages,
    bool HasMore);

public sealed record ChatSendRequest(string? RecipientUserId, Guid ClientMessageId, string? Body);

public sealed record ChatMarkReadRequest(long UpToMessageId);

/// <summary>Sent to the reader's own other tabs so their unread badges stay in step.</summary>
public sealed record ChatReadDto(string PeerUserId, long UpToMessageId);

public sealed record ChatPresenceDto(string UserId, bool IsOnline);
