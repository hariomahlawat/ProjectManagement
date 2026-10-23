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
    bool IsRemoved = false,
    string? RemovalReason = null);

/// <summary>A person's own use of their attachment allowance, and the limits that apply.</summary>
public sealed record ChatAttachmentUsageDto(long UsedBytes, long AllowanceBytes, long MaxFileBytes, int MaxFilesPerMessage);

/// <summary>Storage totals for administrators: sizes only, never names or conversations.</summary>
public sealed record ChatAttachmentTotalsDto(int Files, long TotalBytes, long? FreeDiskBytes, IReadOnlyList<ChatAttachmentPersonUsageDto> LargestUse);

public sealed record ChatAttachmentPersonUsageDto(string DisplayName, long UsedBytes);

/// <summary>
/// A file was removed from a message (the message itself stays). RemovalReason: Sender, Admin or
/// Retention, so the conversation can say how it went.
/// </summary>
public sealed record ChatAttachmentRemovedDto(long AttachmentId, long MessageId, long? GroupId, string? RemovalReason = null);

/// <summary>A removed file was restored within the undo window.</summary>
public sealed record ChatAttachmentRestoredDto(long MessageId, long? GroupId, ChatAttachmentDto Attachment);

// ---------------- Storage management (docs/chat-attachments.md)

/// <summary>
/// One of my sent files. RestorableUntil is set while a removed file can still be restored;
/// ConversationKey opens the chat ("g:{id}" for a group, else the other person's id).
/// </summary>
public sealed record ChatMyFileDto(
    long Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    bool IsImage,
    DateTimeOffset SentUtc,
    string ConversationKey,
    string ConversationName,
    long MessageId,
    DateTimeOffset? RemovedUtc,
    string? RemovalReason,
    DateTimeOffset? RestorableUntil);

public sealed record ChatMyFilesDto(IReadOnlyList<ChatMyFileDto> Files, long UsedBytes, long AllowanceBytes, int UndoHours);

public sealed record ChatFileIdsRequest(IReadOnlyList<long>? Ids);

public sealed record ChatRemoveOlderRequest(int Days);

/// <summary>The outcome of removing or restoring files. Error is set when nothing was done.</summary>
public sealed record ChatStorageChangeDto(int Files, long Bytes, Guid? BatchId, DateTimeOffset? RestorableUntil, string? Error = null);

/// <summary>
/// An administrator clean-up: everyone's files (UserId null) or one person's, optionally only
/// those older than OlderThanDays.
/// </summary>
public sealed record ChatStorageScope(string? UserId, int? OlderThanDays);

public sealed record ChatStoragePreviewDto(int Files, long Bytes, int People);

/// <summary>A clean-up (by an administrator, or automatic retention), undoable until RestorableUntil.</summary>
public sealed record ChatStorageBatchDto(
    Guid BatchId,
    string Reason,
    DateTimeOffset RemovedUtc,
    int Files,
    long Bytes,
    int People,
    bool CanUndo,
    DateTimeOffset RestorableUntil);

public sealed record ChatStoragePersonDto(string UserId, string DisplayName, int Files, long Bytes);

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
