using System;
using System.Collections.Generic;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

public enum ProliferationRecordKind
{
    Yearly,
    Granular
}

public sealed record ProliferationCompletedProjectOption(int Id, string DisplayName);

public sealed record ProliferationSourceOptionVm(int Value, string Label);

public sealed record ProliferationManageBootDefaults(
    int? ProjectId,
    ProliferationSource? Source,
    int? Year,
    ProliferationRecordKind? Kind);

public sealed record ProliferationListBootVm(
    IReadOnlyList<ProliferationCompletedProjectOption> CompletedProjects,
    IReadOnlyList<ProliferationSourceOptionVm> SourceOptions,
    int DefaultPageSize,
    int CurrentYear,
    ProliferationManageBootDefaults Defaults);

public sealed record ProliferationEditorBootVm(
    IReadOnlyList<ProliferationCompletedProjectOption> CompletedProjects,
    IReadOnlyList<ProliferationSourceOptionVm> SourceOptions,
    int CurrentYear,
    ProliferationManageBootDefaults Defaults);

public sealed record ProliferationPreferenceOverridesBootVm(
    IReadOnlyList<ProliferationCompletedProjectOption> CompletedProjects,
    IReadOnlyList<ProliferationSourceOptionVm> SourceOptions,
    int CurrentYear,
    ProliferationManageBootDefaults Defaults);

public sealed record ProliferationManageListRequest(
    int? ProjectId,
    ProliferationSource? Source,
    int? Year,
    ProliferationRecordKind? Kind,
    ApprovalStatus? ApprovalStatus,
    string? Search,
    int Page,
    int PageSize);

public sealed record ProliferationPreferenceOverrideRequest(
    int? ProjectId,
    ProliferationSource? Source,
    int? Year,
    string? Search);

public sealed record ProliferationManageListItem(
    Guid Id,
    ProliferationRecordKind Kind,
    int ProjectId,
    string ProjectName,
    string? ProjectCode,
    ProliferationSource Source,
    string SourceLabel,
    string? UnitName,
    int Year,
    DateOnly? ProliferationDate,
    int Quantity,
    ApprovalStatus ApprovalStatus,
    DateTime CreatedOnUtc,
    DateTime LastUpdatedOnUtc,
    DateTime? ApprovedOnUtc);

public sealed record ProliferationPreferenceOverrideItem(
    Guid Id,
    int ProjectId,
    string ProjectName,
    string? ProjectCode,
    ProliferationSource Source,
    int Year,
    YearPreferenceMode Mode,
    string SetByUserId,
    string SetByDisplayName,
    DateTime SetOnUtc,
    YearPreferenceMode EffectiveMode,
    bool HasYearly,
    bool HasGranular,
    bool HasApprovedYearly,
    bool HasApprovedGranular,
    int EffectiveTotal);

public sealed record ProliferationYearlyDetail(
    Guid Id,
    int ProjectId,
    ProliferationSource Source,
    int Year,
    int TotalQuantity,
    string? Remarks,
    ApprovalStatus ApprovalStatus,
    string SubmittedByUserId,
    string? ApprovedByUserId,
    DateTime? ApprovedOnUtc,
    DateTime CreatedOnUtc,
    DateTime LastUpdatedOnUtc,
    byte[] RowVersion);

public sealed record ProliferationGranularDetail(
    Guid Id,
    int ProjectId,
    ProliferationSource Source,
    DateOnly ProliferationDate,
    string UnitName,
    int Quantity,
    string? Remarks,
    ApprovalStatus ApprovalStatus,
    string SubmittedByUserId,
    string? ApprovedByUserId,
    DateTime? ApprovedOnUtc,
    DateTime CreatedOnUtc,
    DateTime LastUpdatedOnUtc,
    byte[] RowVersion);

public static class ProliferationCompletedProjectOptionExtensions
{
    public static string BuildDisplayName(this Project project)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        return string.IsNullOrWhiteSpace(project.CaseFileNumber)
            ? project.Name
            : $"{project.Name} ({project.CaseFileNumber})";
    }
}
