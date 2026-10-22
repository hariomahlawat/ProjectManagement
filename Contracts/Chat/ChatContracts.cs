using System;
using System.Collections.Generic;

namespace ProjectManagement.Contracts.Chat;

/// <summary>IsOnline is true for online and away; IsAway narrows it (every open tab idle).</summary>
public sealed record ChatContactDto(
    string UserId,
    string DisplayName,
    string? Rank,
    string Initials,
    bool IsActive,
    bool IsOnline,
    int UnreadCount,
    DateTimeOffset? LastMessageUtc,
    bool IsAway = false);

/// <summary>A direct message. Each side works out the other person from the two user ids.</summary>
public sealed record ChatMessageDto(
    long Id,
    string SenderUserId,
    string RecipientUserId,
    Guid ClientMessageId,
    string Body,
    DateTimeOffset SentUtc);

/// <summary>
/// A page of one conversation, oldest first. HasMore: older messages exist before this page.
/// HasNewer: newer messages exist after it (only for pages asked for with "after" or "before"
/// that end short of the latest message). PeerLastReadMessageId drives the read ticks.
/// </summary>
public sealed record ChatHistoryDto(
    string PeerUserId,
    IReadOnlyList<ChatMessageDto> Messages,
    bool HasMore,
    bool HasNewer = false,
    long? PeerLastReadMessageId = null);

public sealed record ChatSendRequest(string? RecipientUserId, Guid ClientMessageId, string? Body);

public sealed record ChatMarkReadRequest(long UpToMessageId);

/// <summary>
/// ConversationRead: to the reader's own other tabs (PeerUserId = the other person).
/// PeerRead: to the other person (PeerUserId = the reader), for read ticks.
/// </summary>
public sealed record ChatReadDto(string PeerUserId, long UpToMessageId);

public sealed record ChatPresenceDto(string UserId, bool IsOnline, bool IsAway = false);

public sealed record ChatTypingDto(string UserId);

/// <summary>One search hit: the message and who the conversation is with.</summary>
public sealed record ChatSearchHitDto(
    long MessageId,
    string PeerUserId,
    string PeerDisplayName,
    string SenderUserId,
    string Body,
    DateTimeOffset SentUtc);
