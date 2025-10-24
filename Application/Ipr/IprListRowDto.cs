using System;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Application.Ipr;

public sealed record IprListRowDto(
    int Id,
    string FilingNumber,
    string? Title,
    IprType Type,
    IprStatus Status,
    DateTimeOffset? FiledAtUtc,
    int? ProjectId,
    string? ProjectName,
    int AttachmentCount,
    string? Notes);
