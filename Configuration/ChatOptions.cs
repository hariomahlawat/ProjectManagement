using System;

namespace ProjectManagement.Configuration;

/// <summary>
/// Person-to-person chat. Turning it off hides the chat UI, refuses the chat API and hub, and
/// leaves stored conversations untouched, so it can be switched back on without data loss.
/// </summary>
public sealed class ChatOptions
{
    public const string SectionName = "Chat";

    public bool Enabled { get; set; } = true;

    /// <summary>Messages one user may send in <see cref="SendWindowSeconds"/>.</summary>
    public int SendPermitLimit { get; set; } = 20;

    public int SendWindowSeconds { get; set; } = 10;

    /// <summary>
    /// Changes one user may make in <see cref="ChangeWindowSeconds"/>: status messages, unsends and
    /// group changes (create, add, remove, rename, leave). Most of these are pushed to many people.
    /// </summary>
    public int ChangePermitLimit { get; set; } = 20;

    public int ChangeWindowSeconds { get; set; } = 60;

    /// <summary>Message searches one user may run in <see cref="SearchWindowSeconds"/>.</summary>
    public int SearchPermitLimit { get; set; } = 60;

    public int SearchWindowSeconds { get; set; } = 60;

    /// <summary>Link-preview requests one user may make in <see cref="PreviewWindowSeconds"/> (each asks for up to 10 links).</summary>
    public int PreviewPermitLimit { get; set; } = 120;

    public int PreviewWindowSeconds { get; set; } = 60;

    /// <summary>How long after sending a message its sender may still unsend it.</summary>
    public int UnsendWindowMinutes { get; set; } = 5;

    /// <summary>File attachments (docs/chat-attachments.md). Off until switched on.</summary>
    public ChatAttachmentOptions Attachments { get; set; } = new();
}

/// <summary>Chat:Attachments. Owner decisions of 2026-10-23 (docs/chat-attachments.md).</summary>
public sealed class ChatAttachmentOptions
{
    /// <summary>Off by default: the attach button, upload and download endpoints appear only when on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Total size of the files one person has sent that are still kept.</summary>
    public int AllowanceMb { get; set; } = 1024;

    public int MaxFileMb { get; set; } = 100;

    /// <summary>Uploads are refused while the upload disk has less than this free.</summary>
    public int MinimumFreeDiskGb { get; set; } = 20;

    public int MaxFilesPerMessage { get; set; } = 5;

    /// <summary>An upload not sent with a message within this time is removed by the clean-up job.</summary>
    public int UnusedUploadMinutes { get; set; } = 60;

    /// <summary>Sent files older than this are removed automatically (owner decision: 1 year). 0 turns it off.</summary>
    public int RetentionDays { get; set; } = 365;

    /// <summary>How long a removal can be undone before the file is deleted from disk (unsending is final).</summary>
    public int UndoHours { get; set; } = 24;

    public TimeSpan UndoWindow => TimeSpan.FromHours(Math.Max(0, UndoHours));

    /// <summary>Uploads one user may start in <see cref="UploadWindowSeconds"/>.</summary>
    public int UploadPermitLimit { get; set; } = 30;

    public int UploadWindowSeconds { get; set; } = 60;

    public long AllowanceBytes => Math.Max(0, AllowanceMb) * 1024L * 1024L;

    public long MaxFileBytes => Math.Max(1, MaxFileMb) * 1024L * 1024L;

    public long MinimumFreeDiskBytes => Math.Max(0, MinimumFreeDiskGb) * 1024L * 1024L * 1024L;
}
