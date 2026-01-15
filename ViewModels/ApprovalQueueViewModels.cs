using System;
using System.Collections.Generic;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;

namespace ProjectManagement.ViewModels;

public enum ApprovalQueueType
{
    StageChange,
    ProjectMeta,
    PlanApproval,
    DocRequest,
    TotRequest,
    ProliferationYearly,
    ProliferationGranular
}

// SECTION: Query + list models
public enum ApprovalQueueModule
{
    Projects,
    ProjectOfficeReports
}

public sealed record ApprovalQueueQuery
{
    public ApprovalQueueType? Type { get; init; }
    public ApprovalQueueModule? Module { get; init; }
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public string? Search { get; init; }
}

public sealed record ApprovalQueueItemVm(
    ApprovalQueueType ApprovalType,
    string RequestId,
    int? ProjectId,
    string? ProjectName,
    string RequestedByUserId,
    string RequestedByName,
    DateTimeOffset RequestedAtUtc,
    string Summary,
    ApprovalQueueModule Module,
    string Status,
    string? DetailsUrl,
    string? ConcurrencyToken = null);

public sealed record ApprovalQueueDetailVm
{
    public ApprovalQueueItemVm Item { get; init; } = default!;
    public StageChangeDetailVm? StageChange { get; init; }
    public ProjectMetaChangeRequestVm? MetaChange { get; init; }
    public PlanApprovalDetailVm? PlanApproval { get; init; }
    public DocumentModerationDetailVm? DocumentModeration { get; init; }
    public TotRequestDetailVm? TotRequest { get; init; }
    public ProliferationYearlyDetailVm? ProliferationYearly { get; init; }
    public ProliferationGranularDetailVm? ProliferationGranular { get; init; }
}

// SECTION: Workflow detail models
public sealed record StageChangeDetailVm(
    string StageCode,
    string StageName,
    string CurrentStatus,
    string RequestedStatus,
    DateOnly? CurrentActualStart,
    DateOnly? CurrentCompletedOn,
    DateOnly? RequestedDate,
    string? RequestNote);

public sealed record PlanApprovalDetailVm(
    IReadOnlyList<PlanStageDiffVm> StageDiffs,
    PlanVersionStatus Status,
    DateTimeOffset? SubmittedOnUtc,
    string? SubmittedBy);

public sealed record PlanStageDiffVm(
    string StageCode,
    string StageName,
    DateOnly? CurrentStart,
    DateOnly? CurrentDue,
    DateOnly? ProposedStart,
    DateOnly? ProposedDue);

public sealed record DocumentModerationDetailVm(
    int RequestId,
    ProjectDocumentRequestType RequestType,
    string Title,
    string? Description,
    string? StageDisplayName,
    string? StageCode,
    string? OriginalFileName,
    string? ContentType,
    long? FileSize,
    DocumentSummaryVm? CurrentDocument,
    string? PreviewUrl,
    string? DownloadUrl);

public sealed record DocumentSummaryVm(
    int DocumentId,
    string Title,
    string OriginalFileName,
    long FileSize,
    string FileStamp,
    DateTimeOffset UploadedAtUtc,
    string UploadedBy,
    bool IsArchived);

public sealed record TotRequestDetailVm(
    ProjectTotStatus CurrentStatus,
    ProjectTotStatus ProposedStatus,
    DateOnly? CurrentStartedOn,
    DateOnly? ProposedStartedOn,
    DateOnly? CurrentCompletedOn,
    DateOnly? ProposedCompletedOn,
    string? CurrentMetDetails,
    string? ProposedMetDetails,
    DateOnly? CurrentMetCompletedOn,
    DateOnly? ProposedMetCompletedOn,
    bool? CurrentFirstProductionModelManufactured,
    bool? ProposedFirstProductionModelManufactured,
    DateOnly? CurrentFirstProductionModelManufacturedOn,
    DateOnly? ProposedFirstProductionModelManufacturedOn);

public sealed record ProliferationYearlyDetailVm(
    Guid RecordId,
    string Source,
    int Year,
    int TotalQuantity,
    string? Remarks,
    ProliferationYearlySnapshotVm? PreviousSnapshot);

public sealed record ProliferationYearlySnapshotVm(
    int TotalQuantity,
    string? Remarks,
    DateTime? ApprovedOnUtc);

public sealed record ProliferationGranularDetailVm(
    Guid RecordId,
    string Source,
    string UnitName,
    DateOnly ProliferationDate,
    int Quantity,
    string? Remarks,
    ProliferationGranularSnapshotVm? PreviousSnapshot);

public sealed record ProliferationGranularSnapshotVm(
    int Quantity,
    string? Remarks,
    DateTime? ApprovedOnUtc);

// SECTION: Decision models
public enum ApprovalDecisionAction
{
    Approve,
    Reject
}

public enum ApprovalDecisionOutcome
{
    Success,
    Forbidden,
    NotFound,
    AlreadyDecided,
    ValidationFailed,
    Error
}

public sealed record ApprovalDecisionRequest(
    ApprovalQueueType ApprovalType,
    string RequestId,
    ApprovalDecisionAction Decision,
    string? Remarks,
    string? RowVersion);

public sealed record ApprovalDecisionResult(ApprovalDecisionOutcome Outcome, string? Message)
{
    public static ApprovalDecisionResult Success()
        => new(ApprovalDecisionOutcome.Success, null);

    public static ApprovalDecisionResult Forbidden(string message)
        => new(ApprovalDecisionOutcome.Forbidden, message);

    public static ApprovalDecisionResult NotFound(string message)
        => new(ApprovalDecisionOutcome.NotFound, message);

    public static ApprovalDecisionResult AlreadyDecided(string message)
        => new(ApprovalDecisionOutcome.AlreadyDecided, message);

    public static ApprovalDecisionResult ValidationFailed(string message)
        => new(ApprovalDecisionOutcome.ValidationFailed, message);

    public static ApprovalDecisionResult Error(string message)
        => new(ApprovalDecisionOutcome.Error, message);
}
