using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Remarks;

namespace ProjectManagement.Services.Ffc;

// SECTION: Progress contracts
public enum FfcProgressSource
{
    ExternalProjectRemark = 0,
    FfcProjectRemark = 1,
    Computed = 2
}

public sealed record FfcProgressTarget(
    long FfcProjectId,
    int? LinkedProjectId,
    string? FfcProjectRemarks);

public sealed record FfcProgressSnapshot(
    long FfcProjectId,
    string? Text,
    int? ExternalRemarkId,
    FfcProgressSource Source,
    bool IsEditable);

public sealed record FfcProgressUpdateCommand(
    long FfcProjectId,
    int? RequestedLinkedProjectId,
    int? ExternalRemarkId,
    string? ProgressText,
    RemarkActorContext? Actor);

public sealed record FfcProgressUpdateResult(
    long FfcProjectId,
    string ProgressText,
    int? ExternalRemarkId,
    FfcProgressSource Source,
    DateTimeOffset UpdatedAt);

public sealed class FfcProgressValidationException : InvalidOperationException
{
    public FfcProgressValidationException(string message)
        : base(message)
    {
    }
}

public sealed class FfcProgressNotFoundException : KeyNotFoundException
{
    public FfcProgressNotFoundException(string message)
        : base(message)
    {
    }
}

public interface IFfcProgressService
{
    Task<IReadOnlyDictionary<long, FfcProgressSnapshot>> GetCurrentProgressAsync(
        IReadOnlyCollection<FfcProgressTarget> targets,
        CancellationToken cancellationToken = default);

    Task<FfcProgressUpdateResult> UpdateProgressAsync(
        FfcProgressUpdateCommand command,
        CancellationToken cancellationToken = default);
}

// SECTION: Canonical progress service
public sealed class FfcProgressService : IFfcProgressService
{
    public const int MaxProgressLength = 2000;

    private readonly ApplicationDbContext _db;
    private readonly IRemarkService _remarkService;
    private readonly ILogger<FfcProgressService> _logger;

    public FfcProgressService(
        ApplicationDbContext db,
        IRemarkService remarkService,
        ILogger<FfcProgressService>? logger = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _remarkService = remarkService ?? throw new ArgumentNullException(nameof(remarkService));
        _logger = logger ?? NullLogger<FfcProgressService>.Instance;
    }

    public async Task<IReadOnlyDictionary<long, FfcProgressSnapshot>> GetCurrentProgressAsync(
        IReadOnlyCollection<FfcProgressTarget> targets,
        CancellationToken cancellationToken = default)
    {
        if (targets is null || targets.Count == 0)
        {
            return new Dictionary<long, FfcProgressSnapshot>();
        }

        var linkedProjectIds = targets
            .Where(target => target.LinkedProjectId.HasValue)
            .Select(target => target.LinkedProjectId!.Value)
            .Distinct()
            .ToArray();

        var latestRemarkByProject = new Dictionary<int, ExternalRemarkTextSnapshot>();
        if (linkedProjectIds.Length > 0)
        {
            var remarks = await _db.Remarks
                .AsNoTracking()
                .Where(remark => linkedProjectIds.Contains(remark.ProjectId)
                    && !remark.IsDeleted
                    && remark.Type == RemarkType.External
                    && remark.Body != null)
                .Select(remark => new ExternalRemarkTextSnapshot(
                    remark.Id,
                    remark.ProjectId,
                    remark.Body,
                    remark.LastEditedAtUtc ?? remark.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            latestRemarkByProject = remarks
                .Where(remark => !string.IsNullOrWhiteSpace(NormalizeProgress(remark.Body)))
                .GroupBy(remark => remark.ProjectId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(remark => remark.SortTimestamp)
                        .ThenByDescending(remark => remark.Id)
                        .First());
        }

        var result = new Dictionary<long, FfcProgressSnapshot>(targets.Count);
        foreach (var target in targets)
        {
            if (target.LinkedProjectId is int linkedProjectId)
            {
                latestRemarkByProject.TryGetValue(linkedProjectId, out var externalRemark);
                result[target.FfcProjectId] = new FfcProgressSnapshot(
                    FfcProjectId: target.FfcProjectId,
                    Text: externalRemark is null ? null : NormalizeProgress(externalRemark.Body),
                    ExternalRemarkId: externalRemark?.Id,
                    Source: FfcProgressSource.ExternalProjectRemark,
                    IsEditable: true);
                continue;
            }

            result[target.FfcProjectId] = new FfcProgressSnapshot(
                FfcProjectId: target.FfcProjectId,
                Text: NormalizeProgress(target.FfcProjectRemarks),
                ExternalRemarkId: null,
                Source: FfcProgressSource.FfcProjectRemark,
                IsEditable: false);
        }

        return result;
    }

    public async Task<FfcProgressUpdateResult> UpdateProgressAsync(
        FfcProgressUpdateCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.FfcProjectId <= 0)
        {
            throw new FfcProgressValidationException("Invalid project identifier.");
        }

        var normalized = NormalizeProgress(command.ProgressText) ?? string.Empty;
        if (normalized.Length > MaxProgressLength)
        {
            throw new FfcProgressValidationException(
                $"Progress text must be {MaxProgressLength} characters or fewer.");
        }

        var project = await _db.FfcProjects
            .Include(item => item.Record)
            .FirstOrDefaultAsync(item => item.Id == command.FfcProjectId, cancellationToken);

        if (project is null || project.Record.IsDeleted)
        {
            throw new FfcProgressNotFoundException("Project row not found.");
        }

        var requestedLinkedProjectId = NormalizeOptionalId(command.RequestedLinkedProjectId);
        var linkedProjectId = NormalizeOptionalId(project.LinkedProjectId);
        var externalRemarkId = NormalizeOptionalId(command.ExternalRemarkId);

        if (requestedLinkedProjectId.HasValue && requestedLinkedProjectId != linkedProjectId)
        {
            throw new FfcProgressValidationException(
                "Linked project reference does not match the selected row.");
        }

        if (linkedProjectId is not int resolvedLinkedProjectId)
        {
            project.Remarks = string.IsNullOrWhiteSpace(normalized) ? null : normalized;
            project.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            return new FfcProgressUpdateResult(
                FfcProjectId: project.Id,
                ProgressText: normalized,
                ExternalRemarkId: null,
                Source: FfcProgressSource.FfcProjectRemark,
                UpdatedAt: project.UpdatedAt);
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new FfcProgressValidationException(
                "Progress text cannot be empty for linked projects.");
        }

        if (command.Actor is null)
        {
            throw new UnauthorizedAccessException(
                "A valid authorised user is required to update linked project progress.");
        }

        var linkedProjectExists = await _db.Projects
            .AsNoTracking()
            .AnyAsync(item => item.Id == resolvedLinkedProjectId, cancellationToken);

        if (!linkedProjectExists)
        {
            throw new FfcProgressValidationException(
                "Linked project not found. Please fix the linkage.");
        }

        var existingRemark = await ResolveExternalRemarkAsync(
            resolvedLinkedProjectId,
            externalRemarkId,
            cancellationToken);

        if (existingRemark is not null)
        {
            try
            {
                var updatedRemark = await _remarkService.EditRemarkAsync(
                    existingRemark.Id,
                    new EditRemarkRequest(
                        Actor: command.Actor,
                        Body: normalized,
                        Scope: existingRemark.Scope,
                        EventDate: existingRemark.EventDate,
                        StageRef: existingRemark.StageRef,
                        StageNameSnapshot: existingRemark.StageNameSnapshot,
                        Meta: BuildFfcMeta(project.FfcRecordId, project.Id, resolvedLinkedProjectId),
                        RowVersion: existingRemark.RowVersion),
                    cancellationToken);

                if (updatedRemark is null)
                {
                    throw new FfcProgressNotFoundException("External remark not found.");
                }

                var updatedAt = ToUtcOffset(
                    updatedRemark.LastEditedAtUtc ?? updatedRemark.CreatedAtUtc);

                return new FfcProgressUpdateResult(
                    FfcProjectId: project.Id,
                    ProgressText: normalized,
                    ExternalRemarkId: updatedRemark.Id,
                    Source: FfcProgressSource.ExternalProjectRemark,
                    UpdatedAt: updatedAt);
            }
            catch (FfcProgressNotFoundException)
            {
                throw;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Preserve the established Detailed Table behaviour: when an existing
                // external remark cannot be edited for an unexpected technical reason,
                // create a fresh canonical external remark instead of losing the update.
                _logger.LogWarning(
                    ex,
                    "Failed to edit external remark from FFC. LinkedProjectId={LinkedProjectId}, ExternalRemarkId={ExternalRemarkId}, FfcProjectId={FfcProjectId}",
                    resolvedLinkedProjectId,
                    existingRemark.Id,
                    project.Id);
            }
        }

        var createdRemark = await _remarkService.CreateRemarkAsync(
            new CreateRemarkRequest(
                ProjectId: resolvedLinkedProjectId,
                Actor: command.Actor,
                Type: RemarkType.External,
                Scope: RemarkScope.General,
                Body: normalized,
                EventDate: DateOnly.FromDateTime(IstClock.ToIst(DateTime.UtcNow)),
                StageRef: null,
                StageNameSnapshot: null,
                Meta: BuildFfcMeta(project.FfcRecordId, project.Id, resolvedLinkedProjectId)),
            cancellationToken);

        return new FfcProgressUpdateResult(
            FfcProjectId: project.Id,
            ProgressText: normalized,
            ExternalRemarkId: createdRemark.Id,
            Source: FfcProgressSource.ExternalProjectRemark,
            UpdatedAt: ToUtcOffset(createdRemark.CreatedAtUtc));
    }

    public static string? NormalizeProgress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    private async Task<ExternalRemarkEditSnapshot?> ResolveExternalRemarkAsync(
        int linkedProjectId,
        int? externalRemarkId,
        CancellationToken cancellationToken)
    {
        if (externalRemarkId.HasValue)
        {
            var byId = await LoadExternalRemarkByIdAsync(
                linkedProjectId,
                externalRemarkId.Value,
                cancellationToken);

            if (byId is not null)
            {
                return byId;
            }
        }

        return await _db.Remarks
            .AsNoTracking()
            .Where(item => item.ProjectId == linkedProjectId
                && !item.IsDeleted
                && item.Type == RemarkType.External
                && item.Body != null
                && item.Body.Trim() != string.Empty)
            .OrderByDescending(item => item.LastEditedAtUtc ?? item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Select(item => new ExternalRemarkEditSnapshot(
                item.Id,
                item.Scope,
                item.EventDate,
                item.StageRef,
                item.StageNameSnapshot,
                item.RowVersion))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<ExternalRemarkEditSnapshot?> LoadExternalRemarkByIdAsync(
        int linkedProjectId,
        int externalRemarkId,
        CancellationToken cancellationToken)
    {
        return _db.Remarks
            .AsNoTracking()
            .Where(item => item.Id == externalRemarkId
                && item.ProjectId == linkedProjectId
                && !item.IsDeleted
                && item.Type == RemarkType.External)
            .Select(item => new ExternalRemarkEditSnapshot(
                item.Id,
                item.Scope,
                item.EventDate,
                item.StageRef,
                item.StageNameSnapshot,
                item.RowVersion))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string BuildFfcMeta(long ffcRecordId, long ffcProjectId, int linkedProjectId)
    {
        return JsonSerializer.Serialize(new
        {
            source = "ProjectOfficeReports.FFC.MapTableDetailed",
            kind = "progress",
            ffcRecordId,
            ffcProjectId,
            linkedProjectId
        });
    }

    private static int? NormalizeOptionalId(int? value)
        => value.HasValue && value.Value > 0 ? value.Value : null;

    private static DateTimeOffset ToUtcOffset(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        return new DateTimeOffset(utc);
    }

    private sealed record ExternalRemarkTextSnapshot(
        int Id,
        int ProjectId,
        string? Body,
        DateTime SortTimestamp);

    private sealed record ExternalRemarkEditSnapshot(
        int Id,
        RemarkScope Scope,
        DateOnly EventDate,
        string? StageRef,
        string? StageNameSnapshot,
        byte[] RowVersion);
}
