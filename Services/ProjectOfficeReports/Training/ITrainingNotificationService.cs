using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.ProjectOfficeReports.Training;

public interface ITrainingNotificationService
{
    Task NotifyDeleteRequestedAsync(TrainingDeleteNotificationContext context, CancellationToken cancellationToken);

    Task NotifyDeleteApprovedAsync(TrainingDeleteNotificationContext context, string approverUserId, CancellationToken cancellationToken);

    Task NotifyDeleteRejectedAsync(TrainingDeleteNotificationContext context, string approverUserId, string decisionNotes, CancellationToken cancellationToken);
}

public sealed record TrainingDeleteNotificationContext(
    Guid TrainingId,
    Guid RequestId,
    string TrainingTypeName,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? TrainingMonth,
    int? TrainingYear,
    int Officers,
    int JuniorCommissionedOfficers,
    int OtherRanks,
    int Total,
    string RequestedByUserId,
    DateTimeOffset RequestedAtUtc,
    string Reason);
