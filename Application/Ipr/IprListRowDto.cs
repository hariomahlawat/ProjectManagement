using System;
using System.Collections.Generic;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Application.Ipr;

public sealed record IprListRowDto(
    int Id,
    string FilingNumber,
    string? Title,
    IprType Type,
    IprStatus Status,
    DateTimeOffset? FiledAtUtc,
    string? FiledBy,
    DateTimeOffset? GrantedAtUtc,
    int? ProjectId,
    string? ProjectName,
    int AttachmentCount,
    IReadOnlyList<IprListAttachmentDto> Attachments,
    string? Notes);
