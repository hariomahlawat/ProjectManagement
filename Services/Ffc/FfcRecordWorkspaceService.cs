using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Ffc;

public sealed record FfcWorkspaceMilestoneDto(
    bool IsCompleted,
    DateOnly? CompletedOn,
    string? Remarks);

public sealed record FfcWorkspaceUnitSummaryDto(
    int InstalledUnits,
    int DeliveredNotInstalledUnits,
    int PlannedUnits,
    FfcCompletionState DeliveryState,
    FfcCompletionState InstallationState)
{
    public int TotalUnits => InstalledUnits + DeliveredNotInstalledUnits + PlannedUnits;

    public int DeliveredUnits => InstalledUnits + DeliveredNotInstalledUnits;
}

public sealed record FfcWorkspaceProjectDto(
    long Id,
    string FfcName,
    int? LinkedProjectId,
    string DisplayName,
    int Quantity,
    FfcUnitPosition Position,
    DateOnly? DeliveredOn,
    DateOnly? InstalledOn,
    string? CurrentProgress,
    ProjectLifecycleStatus? LifecycleStatus,
    string? StageSummary,
    string RowVersion);

public sealed record FfcWorkspaceAttachmentDto(
    long Id,
    FfcAttachmentKind Kind,
    string Label,
    string ContentType,
    long SizeBytes,
    string? UploadedByUserId,
    DateTimeOffset UploadedAt);

public sealed record FfcRecordWorkspaceDto(
    long RecordId,
    long CountryId,
    string CountryName,
    string IsoCode,
    short Year,
    FfcWorkspaceMilestoneDto Ipa,
    FfcWorkspaceMilestoneDto Gsl,
    string? OverallRemarks,
    FfcWorkspaceUnitSummaryDto UnitSummary,
    IReadOnlyList<FfcWorkspaceProjectDto> Projects,
    IReadOnlyList<FfcWorkspaceAttachmentDto> Attachments,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string RowVersion)
{
    public int ProjectCount => Projects.Count;

    public int AttachmentCount => Attachments.Count;
}

public sealed record FfcCountryOptionDto(long Id, string Name, string IsoCode, bool IsActive);

public sealed record FfcArchivedRecordDto(
    long RecordId,
    string CountryName,
    string IsoCode,
    short Year,
    int ProjectCount,
    int AttachmentCount,
    DateTimeOffset ArchivedAt,
    string RowVersion);

public sealed record FfcProjectOptionDto(
    int Id,
    string Name,
    ProjectLifecycleStatus LifecycleStatus,
    string? StageSummary,
    bool IsAvailable)
{
    public string SecondaryText
    {
        get
        {
            var lifecycle = IsAvailable
                ? LifecycleStatus switch
                {
                    ProjectLifecycleStatus.Completed => "Completed",
                    ProjectLifecycleStatus.Cancelled => "Cancelled",
                    _ => "Active"
                }
                : "Unavailable for new links";

            return string.IsNullOrWhiteSpace(StageSummary)
                ? lifecycle
                : $"{lifecycle} · {StageSummary}";
        }
    }
}

public interface IFfcRecordWorkspaceService
{
    Task<FfcRecordWorkspaceDto?> GetAsync(
        long recordId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FfcCountryOptionDto>> GetCountryOptionsAsync(
        long? includeCountryId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FfcProjectOptionDto>> GetProjectOptionsAsync(
        IReadOnlyCollection<int>? includeProjectIds = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FfcArchivedRecordDto>> GetArchivedRecordsAsync(
        CancellationToken cancellationToken = default);
}

public sealed class FfcRecordWorkspaceService : IFfcRecordWorkspaceService
{
    private readonly ApplicationDbContext _db;
    private readonly IFfcProgressService _progressService;

    public FfcRecordWorkspaceService(
        ApplicationDbContext db,
        IFfcProgressService progressService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _progressService = progressService ?? throw new ArgumentNullException(nameof(progressService));
    }

    public async Task<FfcRecordWorkspaceDto?> GetAsync(
        long recordId,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.FfcRecords
            .AsNoTracking()
            .Where(item => item.Id == recordId && !item.IsDeleted)
            .Select(item => new RecordProjection(
                item.Id,
                item.CountryId,
                item.Country.Name,
                item.Country.IsoCode,
                item.Year,
                item.IpaYes,
                item.IpaDate,
                item.IpaRemarks,
                item.GslYes,
                item.GslDate,
                item.GslRemarks,
                item.OverallRemarks,
                item.CreatedAt,
                item.UpdatedAt,
                item.RowVersion))
            .SingleOrDefaultAsync(cancellationToken);

        if (record is null)
        {
            return null;
        }

        var projects = await _db.FfcProjects
            .AsNoTracking()
            .Where(project => project.FfcRecordId == recordId)
            .OrderBy(project => project.Name)
            .ThenBy(project => project.Id)
            .Select(project => new ProjectProjection(
                project.Id,
                project.Name,
                project.Remarks,
                project.LinkedProjectId,
                project.Quantity,
                project.IsDelivered,
                project.DeliveredOn,
                project.IsInstalled,
                project.InstalledOn,
                project.LinkedProject == null ? null : project.LinkedProject.Name,
                project.LinkedProject == null
                    ? null
                    : (ProjectLifecycleStatus?)project.LinkedProject.LifecycleStatus,
                project.RowVersion))
            .ToListAsync(cancellationToken);

        var linkedProjectIds = projects
            .Where(project => project.LinkedProjectId.HasValue)
            .Select(project => project.LinkedProjectId!.Value)
            .Distinct()
            .ToArray();

        var stageSummaryByProjectId = await LoadStageSummariesAsync(linkedProjectIds, cancellationToken);
        var progressByProjectId = await _progressService.GetCurrentProgressAsync(
            projects
                .Select(project => new FfcProgressTarget(
                    project.Id,
                    project.LinkedProjectId,
                    project.Remarks))
                .ToArray(),
            cancellationToken);

        var projectDtos = projects
            .Select(project =>
            {
                progressByProjectId.TryGetValue(project.Id, out var progress);
                string? stageSummary = null;
                if (project.LinkedProjectId.HasValue)
                {
                    stageSummaryByProjectId.TryGetValue(project.LinkedProjectId.Value, out stageSummary);
                }

                return new FfcWorkspaceProjectDto(
                    Id: project.Id,
                    FfcName: project.Name,
                    LinkedProjectId: project.LinkedProjectId,
                    DisplayName: string.IsNullOrWhiteSpace(project.LinkedProjectName)
                        ? project.Name
                        : project.LinkedProjectName,
                    Quantity: project.Quantity,
                    Position: FfcPortfolioQuery.ResolvePosition(project.IsInstalled, project.IsDelivered),
                    DeliveredOn: project.DeliveredOn,
                    InstalledOn: project.InstalledOn,
                    CurrentProgress: progress?.Text,
                    LifecycleStatus: project.LinkedProjectLifecycleStatus,
                    StageSummary: stageSummary,
                    RowVersion: Convert.ToBase64String(project.RowVersion));
            })
            .ToList();

        var attachments = await _db.FfcAttachments
            .AsNoTracking()
            .Where(attachment => attachment.FfcRecordId == recordId)
            .OrderByDescending(attachment => attachment.UploadedAt)
            .ThenByDescending(attachment => attachment.Id)
            .Select(attachment => new FfcWorkspaceAttachmentDto(
                attachment.Id,
                attachment.Kind,
                string.IsNullOrWhiteSpace(attachment.Caption)
                    ? (attachment.Kind == FfcAttachmentKind.Pdf ? "PDF attachment" : "Photo attachment")
                    : attachment.Caption,
                attachment.ContentType,
                attachment.SizeBytes,
                attachment.UploadedByUserId,
                attachment.UploadedAt))
            .ToListAsync(cancellationToken);

        var installed = projectDtos
            .Where(project => project.Position == FfcUnitPosition.Installed)
            .Sum(project => project.Quantity);
        var delivered = projectDtos
            .Where(project => project.Position == FfcUnitPosition.DeliveredAwaitingInstallation)
            .Sum(project => project.Quantity);
        var planned = projectDtos
            .Where(project => project.Position == FfcUnitPosition.Planned)
            .Sum(project => project.Quantity);
        var quantitySummary = new FfcProjectQuantitySummary(installed, delivered, planned);

        return new FfcRecordWorkspaceDto(
            RecordId: record.Id,
            CountryId: record.CountryId,
            CountryName: record.CountryName,
            IsoCode: record.IsoCode,
            Year: record.Year,
            Ipa: new FfcWorkspaceMilestoneDto(record.IpaCompleted, record.IpaDate, record.IpaRemarks),
            Gsl: new FfcWorkspaceMilestoneDto(record.GslCompleted, record.GslDate, record.GslRemarks),
            OverallRemarks: record.OverallRemarks,
            UnitSummary: new FfcWorkspaceUnitSummaryDto(
                InstalledUnits: installed,
                DeliveredNotInstalledUnits: delivered,
                PlannedUnits: planned,
                DeliveryState: FfcPortfolioQuery.ResolveDeliveryState(quantitySummary),
                InstallationState: FfcPortfolioQuery.ResolveInstallationState(quantitySummary)),
            Projects: projectDtos,
            Attachments: attachments,
            CreatedAt: record.CreatedAt,
            UpdatedAt: record.UpdatedAt,
            RowVersion: Convert.ToBase64String(record.RowVersion));
    }

    public async Task<IReadOnlyList<FfcCountryOptionDto>> GetCountryOptionsAsync(
        long? includeCountryId = null,
        CancellationToken cancellationToken = default)
        => await _db.FfcCountries
            .AsNoTracking()
            .Where(country => country.IsActive ||
                              (includeCountryId.HasValue && country.Id == includeCountryId.Value))
            .OrderBy(country => country.Name)
            .Select(country => new FfcCountryOptionDto(
                country.Id,
                country.Name,
                country.IsoCode,
                country.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FfcProjectOptionDto>> GetProjectOptionsAsync(
        IReadOnlyCollection<int>? includeProjectIds = null,
        CancellationToken cancellationToken = default)
    {
        var includedIds = includeProjectIds?
            .Distinct()
            .ToArray() ?? Array.Empty<int>();

        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project =>
                (!project.IsDeleted && !project.IsBuild) || includedIds.Contains(project.Id))
            .OrderBy(project => project.Name)
            .Select(project => new ProjectOptionProjection(
                project.Id,
                project.Name,
                project.LifecycleStatus,
                !project.IsDeleted && !project.IsBuild))
            .ToListAsync(cancellationToken);

        var projectIds = projects.Select(project => project.Id).ToArray();
        var stageSummaries = await LoadStageSummariesAsync(projectIds, cancellationToken);

        return projects
            .Select(project =>
            {
                stageSummaries.TryGetValue(project.Id, out var stageSummary);
                return new FfcProjectOptionDto(
                    project.Id,
                    project.Name,
                    project.LifecycleStatus,
                    stageSummary,
                    project.IsAvailable);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<FfcArchivedRecordDto>> GetArchivedRecordsAsync(
        CancellationToken cancellationToken = default)
    {
        var records = await _db.FfcRecords
            .AsNoTracking()
            .Where(record => record.IsDeleted)
            .OrderByDescending(record => record.UpdatedAt)
            .ThenBy(record => record.Country.Name)
            .ThenByDescending(record => record.Year)
            .Select(record => new ArchivedRecordProjection(
                record.Id,
                record.Country.Name,
                record.Country.IsoCode,
                record.Year,
                record.Projects.Count,
                record.Attachments.Count,
                record.UpdatedAt,
                record.RowVersion))
            .ToListAsync(cancellationToken);

        return records
            .Select(record => new FfcArchivedRecordDto(
                record.RecordId,
                record.CountryName,
                record.IsoCode,
                record.Year,
                record.ProjectCount,
                record.AttachmentCount,
                record.ArchivedAt,
                Convert.ToBase64String(record.RowVersion)))
            .ToList();
    }

    private async Task<Dictionary<int, string?>> LoadStageSummariesAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return new Dictionary<int, string?>();
        }

        var stages = await _db.ProjectStages
            .AsNoTracking()
            .Where(stage => projectIds.Contains(stage.ProjectId))
            .Select(stage => new FfcProjectStageSnapshot(
                stage.ProjectId,
                stage.StageCode,
                stage.SortOrder,
                stage.Status,
                stage.CompletedOn))
            .ToListAsync(cancellationToken);

        return stages
            .GroupBy(stage => stage.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => FfcProjectStageSummaryFormatter.Format(group));
    }

    private sealed record RecordProjection(
        long Id,
        long CountryId,
        string CountryName,
        string IsoCode,
        short Year,
        bool IpaCompleted,
        DateOnly? IpaDate,
        string? IpaRemarks,
        bool GslCompleted,
        DateOnly? GslDate,
        string? GslRemarks,
        string? OverallRemarks,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        byte[] RowVersion);

    private sealed record ProjectProjection(
        long Id,
        string Name,
        string? Remarks,
        int? LinkedProjectId,
        int Quantity,
        bool IsDelivered,
        DateOnly? DeliveredOn,
        bool IsInstalled,
        DateOnly? InstalledOn,
        string? LinkedProjectName,
        ProjectLifecycleStatus? LinkedProjectLifecycleStatus,
        byte[] RowVersion);

    private sealed record ArchivedRecordProjection(
        long RecordId,
        string CountryName,
        string IsoCode,
        short Year,
        int ProjectCount,
        int AttachmentCount,
        DateTimeOffset ArchivedAt,
        byte[] RowVersion);

    private sealed record ProjectOptionProjection(
        int Id,
        string Name,
        ProjectLifecycleStatus LifecycleStatus,
        bool IsAvailable);
}
