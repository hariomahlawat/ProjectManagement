using System;

namespace ProjectManagement.Application.Ipr;

public sealed record IprListAttachmentDto(
    int Id,
    string FileName,
    long FileSize,
    string UploadedBy,
    DateTimeOffset UploadedAtUtc);
