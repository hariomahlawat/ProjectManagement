using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.Reports.ProgressReview;

public interface IProgressReviewService
{
    Task<ProgressReviewVm> GetAsync(ProgressReviewRequest request, CancellationToken cancellationToken = default);
}

public sealed record ProgressReviewRequest(DateOnly From, DateOnly To);

public sealed record ProgressReviewVm(
    RangeVm Range,
    ProjectSectionVm Projects,
    VisitSectionVm Visits,
    SocialMediaSectionVm SocialMedia,
    TotSectionVm Tot,
    IprSectionVm Ipr,
    TrainingSectionVm Training,
    ProliferationSectionVm Proliferation,
    FfcSectionVm Ffc,
    MiscSectionVm Misc,
    TotalsVm Totals
);

public sealed record RangeVm(DateOnly From, DateOnly To);

public sealed record ProjectSectionVm(
    IReadOnlyList<ProjectStageChangeVm> FrontRunners,
    IReadOnlyList<ProjectRemarkOnlyVm> WorkInProgress,
    IReadOnlyList<ProjectNonMoverVm> NonMovers,
    IReadOnlyList<ProjectProgressRowVm> SummaryRows,
    IReadOnlyList<ProjectCategoryGroupVm> CategoryGroups
);

public sealed record ProjectStageChangeVm(
    int ProjectId,
    string ProjectName,
    string StageCode,
    string StageName,
    string? FromStatus,
    string? ToStatus,
    DateOnly ChangeDate,
    DateOnly? ToActualStart,
    DateOnly? ToCompletedOn
);

public sealed record ProjectRemarkOnlyVm(
    int ProjectId,
    string ProjectName,
    ProjectRemarkSummaryVm RemarkSummary
);

public sealed record ProjectNonMoverVm(
    int ProjectId,
    string ProjectName,
    string StageCode,
    string StageName,
    int DaysSinceActivity
);

public sealed record ProjectProgressRowVm(
    int ProjectId,
    string ProjectName,
    string? ProjectCategoryName,
    PresentStageSnapshot PresentStage,
    IReadOnlyList<ProjectStageMovementVm> StageMovements,
    int StageMovementOverflowCount,
    ProjectRemarkSummaryVm RemarkSummary
);

public sealed record ProjectCategoryGroupVm(
    string CategoryName,
    IReadOnlyList<ProjectProgressRowVm> Projects
);

public sealed record ProjectStageMovementVm(
    string StageName,
    bool IsOngoing,
    DateOnly? StartedOn,
    DateOnly? CompletedOn
);

public sealed record ProjectRemarkSummaryVm(
    DateOnly? LatestRemarkDate,
    string? LatestRemarkSummary,
    RemarkActorRole? LatestRemarkAuthorRole,
    int MoreRemarkCount)
{
    public static ProjectRemarkSummaryVm Empty { get; } = new(null, null, null, 0);
}

public sealed record VisitSectionVm(IReadOnlyList<VisitSummaryVm> Items, int TotalCount);

public sealed record VisitSummaryVm(
    Guid Id,
    DateOnly Date,
    string VisitorName,
    string VisitType,
    int Strength,
    string? Remarks,
    Guid? CoverPhotoId,
    Guid? DisplayPhotoId
);

public sealed record SocialMediaSectionVm(IReadOnlyList<SocialMediaPostVm> Items, int TotalCount);

public sealed record SocialMediaPostVm(
    Guid Id,
    DateOnly Date,
    string Title,
    string Platform,
    string? Description,
    Guid? CoverPhotoId,
    Guid? DisplayPhotoId
);

public sealed record TotSectionVm(
    IReadOnlyList<TotStageChangeVm> StageChanges,
    IReadOnlyList<TotRemarkVm> Remarks
);

public sealed record TotStageChangeVm(
    int ProjectId,
    string ProjectName,
    string StageCode,
    string StageName,
    string? FromStatus,
    string? ToStatus,
    DateOnly ChangeDate
);

public sealed record TotRemarkVm(
    int ProjectId,
    string ProjectName,
    DateOnly Date,
    string? Summary
);

public sealed record IprSectionVm(
    IReadOnlyList<IprStatusChangeVm> StatusChanges,
    IReadOnlyList<IprRemarkVm> Remarks
);

public sealed record IprStatusChangeVm(
    int IprId,
    string Title,
    IprStatus Status,
    DateOnly EventDate,
    string? Notes
);

public sealed record IprRemarkVm(
    int IprId,
    string Title,
    DateOnly EventDate,
    string? Summary
);

public sealed record TrainingSectionVm(
    TrainingBlockVm Simulator,
    TrainingBlockVm Drone
);

public sealed record TrainingBlockVm(
    int TotalPersons,
    IReadOnlyList<TrainingRowVm> Rows
);

public sealed record TrainingRowVm(
    Guid TrainingId,
    DateOnly Date,
    string Title,
    string UnitOrOrg,
    int Persons
);

public sealed record ProliferationSectionVm(IReadOnlyList<ProliferationRowVm> Rows);

public sealed record ProliferationRowVm(
    Guid EntryId,
    int ProjectId,
    string ProjectName,
    string UnitOrCountry,
    ProliferationSource Source,
    ApprovalStatus Status,
    DateOnly Date,
    int Quantity,
    string? Remarks
);

public sealed record FfcSectionVm(IReadOnlyList<FfcProgressVm> Rows);

public sealed record FfcProgressVm(
    long RecordId,
    string Country,
    string Milestone,
    DateOnly Date,
    string? Remarks
);

public sealed record MiscSectionVm(IReadOnlyList<MiscActivityVm> Rows);

public sealed record MiscActivityVm(
    int ActivityId,
    DateOnly Date,
    string Title,
    string? Summary,
    string? Location,
    string? PhotoUrl
);

public sealed record TotalsVm(
    int ProjectsMoved,
    int ProjectsWithRemarks,
    int NonMovers,
    int VisitsCount,
    int SocialPostsCount,
    int TotChangesCount,
    int IprChangesCount,
    int SimulatorTrainees,
    int DroneTrainees,
    int ProliferationsCount,
    int FfcItemsChanged,
    int MiscCount
);
