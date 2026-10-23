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
    bool IsAway = false,
    string? StatusMessage = null);

/// <summary>
/// A direct message. Each side works out the other person from the two user ids. An unsent
/// message keeps its place with IsUnsent and an empty body.
/// </summary>
public sealed record ChatMessageDto(
    long Id,
    string SenderUserId,
    string RecipientUserId,
    Guid ClientMessageId,
    string Body,
    DateTimeOffset SentUtc,
    bool IsUnsent = false,
    long? GroupId = null,
    string? EventKind = null,
    string? EventData = null,
    IReadOnlyList<ChatAttachmentDto>? Attachments = null);

/// <summary>
/// A file sent with a message. IsRemoved: removed by its sender or unsent with its message; the
/// name is then withheld and the file can no longer be downloaded.
/// </summary>
public sealed record ChatAttachmentDto(
    long Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    bool IsImage,
    int? Width,
    int? Height,
    bool IsRemoved = false);

/// <summary>A person's own use of their attachment allowance, and the limits that apply.</summary>
public sealed record ChatAttachmentUsageDto(long UsedBytes, long AllowanceBytes, long MaxFileBytes, int MaxFilesPerMessage);

/// <summary>Storage totals for administrators: sizes only, never names or conversations.</summary>
public sealed record ChatAttachmentTotalsDto(int Files, long TotalBytes, long? FreeDiskBytes, IReadOnlyList<ChatAttachmentPersonUsageDto> LargestUse);

public sealed record ChatAttachmentPersonUsageDto(string DisplayName, long UsedBytes);

/// <summary>A sender removed a file from a message (the message itself stays).</summary>
public sealed record ChatAttachmentRemovedDto(long AttachmentId, long MessageId, long? GroupId);

/// <summary>Files removed from messages since a time, for a page that missed the live events.</summary>
public sealed record ChatRemovedFilesPageDto(IReadOnlyList<ChatAttachmentRemovedDto> Files, bool HasMore);

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

/// <summary>AttachmentIds: files uploaded first (POST /api/chat/attachments), sent with this message.</summary>
public sealed record ChatSendRequest(string? RecipientUserId, Guid ClientMessageId, string? Body, IReadOnlyList<long>? AttachmentIds = null);

public sealed record ChatMarkReadRequest(long UpToMessageId);

/// <summary>
/// ConversationRead: to the reader's own other tabs (PeerUserId = the other person).
/// PeerRead: to the other person (PeerUserId = the reader), for read ticks.
/// </summary>
public sealed record ChatReadDto(string PeerUserId, long UpToMessageId);

public sealed record ChatPresenceDto(string UserId, bool IsOnline, bool IsAway = false);

public sealed record ChatTypingDto(string UserId);

/// <summary>A person's status message; null Message means cleared.</summary>
public sealed record ChatStatusDto(string UserId, string? Message);

public sealed record ChatStatusRequest(string? Message);

/// <summary>
/// Unsent message ids, newest first. HasMore: ask again with before = the smallest id here.
/// </summary>
public sealed record ChatUnsentPageDto(IReadOnlyList<long> Ids, bool HasMore);

/// <summary>
/// One search hit: the message and its conversation (PeerUserId for a direct one, GroupId for a
/// group; PeerDisplayName carries the person's or the group's name).
/// </summary>
public sealed record ChatSearchHitDto(
    long MessageId,
    string PeerUserId,
    string PeerDisplayName,
    string SenderUserId,
    string Body,
    DateTimeOffset SentUtc,
    long? GroupId = null);

/// <summary>
/// A preview card for a link to a PRISM record in a message, built for the person asking.
/// Link is the link as asked for (path and query); Kind is project, document, task, idea or
/// repository-document; ImageUrl, when set, is a small picture (a project's cover photo).
/// </summary>
public sealed record ChatLinkPreviewDto(string Link, string Kind, string Title, string? Subtitle, string? ImageUrl);

// ---------------- Groups

public sealed record ChatGroupSummaryDto(
    long Id,
    string Title,
    string ManagerUserId,
    int MemberCount,
    int UnreadCount,
    DateTimeOffset? LastMessageUtc);

/// <summary>
/// JoinedUtc: when this membership started. A member sees only messages sent from then on, so
/// "Seen by" for an older message leaves them out.
/// </summary>
public sealed record ChatGroupMemberDto(
    string UserId,
    string DisplayName,
    string Initials,
    bool IsActive,
    long? LastReadMessageId,
    DateTimeOffset? JoinedUtc = null);

public sealed record ChatGroupDto(long Id, string Title, string ManagerUserId, IReadOnlyList<ChatGroupMemberDto> Members);

public sealed record ChatGroupHistoryDto(
    long GroupId,
    IReadOnlyList<ChatMessageDto> Messages,
    bool HasMore,
    bool HasNewer,
    IReadOnlyList<ChatGroupMemberDto> Members);

public sealed record ChatGroupCreateRequest(string? Title, IReadOnlyList<string>? MemberUserIds);

public sealed record ChatGroupMembersRequest(IReadOnlyList<string>? UserIds);

public sealed record ChatGroupRenameRequest(string? Title);

public sealed record ChatGroupSendRequest(Guid ClientMessageId, string? Body, IReadOnlyList<long>? AttachmentIds = null);

/// <summary>Someone in a group read up to a message (for "Seen by").</summary>
public sealed record ChatGroupReadDto(long GroupId, string UserId, long UpToMessageId);

public sealed record ChatGroupTypingDto(long GroupId, string UserId);
