using System;
using System.Collections.Generic;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Application.Ipr;

public sealed record IprExportRowDto(
    int Id,
    string FilingNumber,
    string? Title,
    IprStatus Status,
    string? FiledBy,
    DateTimeOffset? FiledAtUtc,
    DateTimeOffset? GrantedAtUtc,
    string? ProjectName,
    string? Remarks,
    IReadOnlyList<IprExportAttachmentDto> Attachments);

public sealed record IprExportAttachmentDto(
    int Id,
    string FileName,
    string ContentType,
    long FileSize,
    string UploadedBy,
    DateTimeOffset UploadedAtUtc);
