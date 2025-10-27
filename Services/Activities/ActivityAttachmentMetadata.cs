using System;

namespace ProjectManagement.Services.Activities;

public sealed record ActivityAttachmentMetadata(
    int Id,
    string FileName,
    string ContentType,
    long FileSize,
    string DownloadUrl,
    string StorageKey,
    DateTimeOffset UploadedAtUtc,
    string UploadedByUserId);
