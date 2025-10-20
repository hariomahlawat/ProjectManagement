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

public sealed record ProliferationListBootVm(
    IReadOnlyList<ProliferationCompletedProjectOption> CompletedProjects,
    IReadOnlyList<ProliferationSourceOptionVm> SourceOptions,
    int DefaultPageSize,
    int CurrentYear);

public sealed record ProliferationEditorBootVm(
    IReadOnlyList<ProliferationCompletedProjectOption> CompletedProjects,
    IReadOnlyList<ProliferationSourceOptionVm> SourceOptions,
    int CurrentYear);

public sealed record ProliferationManageListRequest(
    int? ProjectId,
    ProliferationSource? Source,
    int? Year,
    ProliferationRecordKind? Kind,
    int Page,
    int PageSize);

public sealed record ProliferationManageListItem(
    Guid Id,
    ProliferationRecordKind Kind,
    int ProjectId,
    string ProjectName,
    string? ProjectCode,
    ProliferationSource Source,
    string SourceLabel,
    int Year,
    DateOnly? ProliferationDate,
    int Quantity,
    ApprovalStatus ApprovalStatus,
    DateTime CreatedOnUtc,
    DateTime LastUpdatedOnUtc);

public sealed record ProliferationYearlyDetail(
    Guid Id,
    int ProjectId,
    ProliferationSource Source,
    int Year,
    int TotalQuantity,
    string? Remarks,
    byte[] RowVersion);

public sealed record ProliferationGranularDetail(
    Guid Id,
    int ProjectId,
    ProliferationSource Source,
    DateOnly ProliferationDate,
    string SimulatorName,
    string UnitName,
    int Quantity,
    string? Remarks,
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
