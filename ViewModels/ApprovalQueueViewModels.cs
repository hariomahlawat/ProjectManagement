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
    ProliferationGranular,
    ActivityDelete,
    TrainingDelete,
    RepositoryDocumentDelete
}

// SECTION: Query + list models
public enum ApprovalQueueModule
{
    Projects,
    ProjectOfficeReports,
    Activities,
    DocumentRepository
}

public enum ApprovalReadiness
{
    Ready,
    Waiting,
    Blocked,
    Superseded,
    Stale
}

public enum ApprovalCheckState
{
    Passed,
    Waiting,
    Blocked,
    Warning
}

public sealed record ApprovalQueueQuery
{
    public ApprovalQueueType? Type { get; init; }
    public ApprovalQueueModule? Module { get; init; }
    public ApprovalReadiness? Readiness { get; init; }
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
    string? ConcurrencyToken = null,
    ApprovalReadiness Readiness = ApprovalReadiness.Ready,
    string? ReadinessMessage = null,
    string? WorkflowVersion = null,
    string? StageCode = null,
    int? WorkflowOrder = null,
    int RevisionNumber = 1,
    int RelatedRequestCount = 0,
    string? CorrectionUrl = null);

public sealed record ApprovalQueueGroupVm(
    int? ProjectId,
    string Title,
    string? Subtitle,
    string? WorkflowVersion,
    IReadOnlyList<ApprovalQueueItemVm> Items,
    int ReadyCount,
    int WaitingCount,
    int BlockedCount);

public sealed record ApprovalCheckVm(
    ApprovalCheckState State,
    string Label,
    string? Detail = null,
    string? ActionLabel = null,
    string? ActionUrl = null);

public sealed record RelatedApprovalVm(
    ApprovalQueueType ApprovalType,
    string RequestId,
    string Label,
    string Summary,
    string Status,
    ApprovalReadiness Readiness,
    DateTimeOffset RequestedAtUtc,
    string? DetailsUrl);

public sealed record ApprovalQueueDetailVm
{
    public ApprovalQueueItemVm Item { get; init; } = default!;
    public IReadOnlyList<ApprovalCheckVm> ReadinessChecks { get; init; } = Array.Empty<ApprovalCheckVm>();
    public IReadOnlyList<RelatedApprovalVm> RelatedRequests { get; init; } = Array.Empty<RelatedApprovalVm>();
    public StageChangeDetailVm? StageChange { get; init; }
    public ProjectMetaChangeRequestVm? MetaChange { get; init; }
    public PlanApprovalDetailVm? PlanApproval { get; init; }
    public DocumentModerationDetailVm? DocumentModeration { get; init; }
    public TotRequestDetailVm? TotRequest { get; init; }
    public ProliferationYearlyDetailVm? ProliferationYearly { get; init; }
    public ProliferationGranularDetailVm? ProliferationGranular { get; init; }
    public ActivityDeleteDetailVm? ActivityDelete { get; init; }
    public TrainingDeleteDetailVm? TrainingDelete { get; init; }
    public RepositoryDocumentDeleteDetailVm? RepositoryDocumentDelete { get; init; }
}

// SECTION: Workflow detail models
public sealed record StageChangeDetailVm(
    string StageCode,
    string StageName,
    string WorkflowVersion,
    int WorkflowOrder,
    int RevisionNumber,
    bool IsLatestRevision,
    string CurrentStatus,
    string RequestedStatus,
    DateOnly? CurrentActualStart,
    DateOnly? CurrentCompletedOn,
    DateOnly? RequestedStartDate,
    DateOnly? RequestedDate,
    string? RequestNote);

public sealed record PlanApprovalDetailVm(
    int PlanVersionId,
    int VersionNumber,
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
    int FileStamp,
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

public sealed record ActivityDeleteDetailVm(
    int ActivityId,
    string Title,
    string ActivityType,
    string? Location,
    DateTimeOffset? ScheduledStartUtc,
    string? Reason);

public sealed record TrainingDeleteDetailVm(
    Guid TrainingId,
    string TrainingType,
    string Period,
    int TotalTrainees,
    string Reason);

public sealed record RepositoryDocumentDeleteDetailVm(
    Guid DocumentId,
    string Subject,
    string? ReceivedFrom,
    DateOnly? DocumentDate,
    string OriginalFileName,
    long FileSizeBytes,
    string? Reason);

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
    public static ApprovalDecisionResult Success(string? message = null)
        => new(ApprovalDecisionOutcome.Success, message);

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
