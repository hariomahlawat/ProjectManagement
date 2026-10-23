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

    /// <summary>When the clean-up job deleted the file (and thumbnail) from disk.</summary>
    public DateTimeOffset? PurgedUtc { get; set; }

    public bool IsImage => ContentType.StartsWith("image/", StringComparison.Ordinal);
}
