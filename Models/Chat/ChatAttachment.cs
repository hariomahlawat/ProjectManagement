using System;

namespace ProjectManagement.Models.Chat;

/// <summary>
/// A file sent with a chat message (docs/chat-attachments.md). Uploaded first (MessageId null),
/// then sent with a message. The file lives under the upload root at <see cref="StorageKey"/>,
/// a random name; the uploaded name is kept only here, for display and downloads.
/// </summary>
public sealed class ChatAttachment
{
    public const int MaxFileNameLength = 255;

    public long Id { get; set; }

    public string UploaderUserId { get; set; } = string.Empty;

    /// <summary>Null until the file is sent with a message.</summary>
    public long? MessageId { get; set; }

    public ChatMessage? Message { get; set; }

    /// <summary>Relative to the upload root (chat/yyyy/MM/{random}.ext).</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>A small WebP for images, beside the file; null for other files.</summary>
    public string? ThumbnailKey { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>Decided by PRISM from the checked file type, never taken from the browser.</summary>
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public int? Width { get; set; }

    public int? Height { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>
    /// Removed by its sender, unsent with its message, or never sent. From then on it no longer
    /// counts against the allowance and cannot be downloaded; the clean-up job deletes the file.
    /// </summary>
    public DateTimeOffset? RemovedUtc { get; set; }

    /// <summary>
    /// When the clean-up job deleted the file (and thumbnail) from disk. While the job is
    /// deleting it this holds <see cref="PurgeClaimed"/>: from the claim on, it cannot be
    /// restored, and a claim a sweep could not finish is finished by a later one.
    /// </summary>
    public DateTimeOffset? PurgedUtc { get; set; }

    /// <summary><see cref="PurgedUtc"/> of a file claimed for deletion and not yet confirmed gone.</summary>
    public static readonly DateTimeOffset PurgeClaimed = DateTimeOffset.UnixEpoch;

    /// <summary>Why it was removed (<see cref="ChatAttachmentRemovalReason"/>); null while kept.</summary>
    public string? RemovalReason { get; set; }

    /// <summary>Who removed it (the sender, or an administrator); null for automatic removals.</summary>
    public string? RemovedByUserId { get; set; }

    /// <summary>
    /// Removals made together (one administrator clean-up, one retention run, one "remove
    /// selected") share an id, so they can be undone together within the undo window.
    /// </summary>
    public Guid? RemovalBatchId { get; set; }

    public bool IsImage => ContentType.StartsWith("image/", StringComparison.Ordinal);
}

/// <summary>Why a chat attachment was removed. Undo is possible for a day, except after unsending.</summary>
public static class ChatAttachmentRemovalReason
{
    /// <summary>Its sender removed it.</summary>
    public const string Sender = "Sender";

    /// <summary>An administrator's storage clean-up.</summary>
    public const string Administrator = "Admin";

    /// <summary>Automatic retention (older than Chat:Attachments:RetentionDays).</summary>
    public const string Retention = "Retention";

    /// <summary>Its message was unsent; final, no undo.</summary>
    public const string Unsent = "Unsent";

    /// <summary>Uploaded but never sent with a message; final.</summary>
    public const string Unused = "Unused";

    public const int MaxLength = 16;

    /// <summary>Removals that can still be undone within the undo window.</summary>
    public static bool CanUndo(string? reason) => reason is Sender or Administrator or Retention;
}
