using System;

namespace ProjectManagement.Services.ActionTasks;

public sealed record ActionTaskAttachmentMetadata(
    int Id,
    int TaskId,
    int? UpdateId,
    string FileName,
    string ContentType,
    long FileSize,
    string DownloadUrl,
    string InlineUrl,
    DateTime UploadedAtUtc,
    string UploadedByUserId);
