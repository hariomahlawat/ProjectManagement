using System;

namespace ProjectManagement.Services.Activities;

public sealed record ActivityDeleteRequestSummary(
    int Id,
    int ActivityId,
    string ActivityTitle,
    string ActivityTypeName,
    string? ActivityLocation,
    DateTimeOffset? ScheduledStartUtc,
    DateTimeOffset RequestedAtUtc,
    string RequestedByUserId,
    string? RequestedByDisplayName,
    string? RequestedByEmail,
    string? Reason,
    byte[] RowVersion);

public sealed record ActivityDeleteNotificationContext(
    int RequestId,
    int ActivityId,
    string ActivityTitle,
    string ActivityTypeName,
    string? ActivityLocation,
    DateTimeOffset? ScheduledStartUtc,
    DateTimeOffset RequestedAtUtc,
    string RequestedByUserId,
    string? RequestedByDisplayName,
    string? RequestedByEmail,
    string? Reason);
