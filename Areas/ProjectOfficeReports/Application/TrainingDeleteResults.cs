using System;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public enum TrainingDeleteFailureCode
{
    None = 0,
    TrainingNotFound,
    ConcurrencyConflict,
    PendingRequestExists,
    MissingUserId,
    InvalidReason,
    RequestNotFound,
    RequestNotPending
}

public sealed record TrainingDeleteRequestResult(
    bool IsSuccess,
    TrainingDeleteFailureCode FailureCode,
    string? ErrorMessage,
    Guid? RequestId)
{
    public static TrainingDeleteRequestResult Success(Guid requestId)
        => new(true, TrainingDeleteFailureCode.None, null, requestId);

    public static TrainingDeleteRequestResult Failure(TrainingDeleteFailureCode failureCode, string? errorMessage = null)
        => new(false, failureCode, errorMessage, null);
}

public sealed record TrainingDeleteDecisionResult(
    bool IsSuccess,
    TrainingDeleteFailureCode FailureCode,
    string? ErrorMessage)
{
    public static TrainingDeleteDecisionResult Success()
        => new(true, TrainingDeleteFailureCode.None, null);

    public static TrainingDeleteDecisionResult Failure(TrainingDeleteFailureCode failureCode, string? errorMessage = null)
        => new(false, failureCode, errorMessage);
}
