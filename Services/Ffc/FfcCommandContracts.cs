using System;
using System.Collections.Generic;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Remarks;

namespace ProjectManagement.Services.Ffc;

public sealed record FfcCommandResult(
    bool Success,
    long? EntityId = null,
    string? Message = null,
    bool IsConcurrencyConflict = false,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null)
{
    public static FfcCommandResult Ok(long? entityId = null, string? message = null)
        => new(true, entityId, message);

    public static FfcCommandResult Invalid(
        string? message = null,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null)
        => new(false, null, message, false, fieldErrors);

    public static FfcCommandResult Conflict(string message)
        => new(false, null, message, true);
}

public sealed record FfcRecordCreateCommand(
    long CountryId,
    short Year,
    bool IpaCompleted,
    DateOnly? IpaDate,
    string? IpaRemarks,
    bool GslCompleted,
    DateOnly? GslDate,
    string? GslRemarks,
    string? OverallRemarks,
    string? CreatedByUserId);

public sealed record FfcRecordUpdateCommand(
    long RecordId,
    long CountryId,
    short Year,
    bool IpaCompleted,
    DateOnly? IpaDate,
    string? IpaRemarks,
    bool GslCompleted,
    DateOnly? GslDate,
    string? GslRemarks,
    string? OverallRemarks,
    string? RowVersion);

public sealed record FfcProjectSaveCommand(
    long RecordId,
    long? ProjectId,
    bool IsLinkedProject,
    string? DisplayName,
    int? LinkedProjectId,
    int Quantity,
    FfcUnitPosition Position,
    DateOnly? DeliveredOn,
    DateOnly? InstalledOn,
    string? ProgressText,
    string? RowVersion,
    RemarkActorContext? Actor);
