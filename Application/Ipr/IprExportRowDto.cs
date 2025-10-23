using System;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Application.Ipr;

public sealed record IprExportRowDto(
    string FilingNumber,
    string? Title,
    IprStatus Status,
    string? FiledBy,
    DateTimeOffset? FiledAtUtc,
    DateTimeOffset? GrantedAtUtc,
    string? ProjectName,
    string? Remarks);
