using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectModerationService
{
    private static class AuditActions
    {
        public const string Archive = "Archive";
        public const string RestoreArchive = "RestoreArchive";
        public const string Trash = "Trash";
        public const string RestoreTrash = "RestoreTrash";
        public const string Purge = "Purge";
    }

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<ProjectModerationService> _logger;
    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly ProjectDocumentOptions _documentOptions;

    public ProjectModerationService(
        ApplicationDbContext db,
        IClock clock,
        ILogger<ProjectModerationService> logger,
        IUploadRootProvider uploadRootProvider,
        IOptions<ProjectDocumentOptions> documentOptions)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
        _documentOptions = documentOptions?.Value ?? throw new ArgumentNullException(nameof(documentOptions));
    }

    public async Task<ProjectModerationResult> ArchiveAsync(
        int projectId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectAsync(projectId, cancellationToken);
        if (project is null)
        {
            return ProjectModerationResult.NotFound();
        }

        if (project.IsDeleted)
        {
            return ProjectModerationResult.InvalidState("Project is in Trash and cannot be archived.");
        }

        if (project.IsArchived)
        {
            return ProjectModerationResult.Success();
        }

        project.IsArchived = true;
        project.ArchivedAt = _clock.UtcNow;
        project.ArchivedByUserId = actorUserId;

        await WriteAuditAsync(project.Id, AuditActions.Archive, actorUserId, reason: null, metadata: null, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return ProjectModerationResult.Success();
    }

    public async Task<ProjectModerationResult> RestoreFromArchiveAsync(
        int projectId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectAsync(projectId, cancellationToken);
        if (project is null)
        {
            return ProjectModerationResult.NotFound();
        }

        if (project.IsDeleted)
        {
            return ProjectModerationResult.InvalidState("Project is in Trash and cannot be restored from archive.");
        }

        if (!project.IsArchived)
        {
            return ProjectModerationResult.Success();
        }

        project.IsArchived = false;
        project.ArchivedAt = null;
        project.ArchivedByUserId = null;

        await WriteAuditAsync(project.Id, AuditActions.RestoreArchive, actorUserId, reason: null, metadata: null, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return ProjectModerationResult.Success();
    }

    public async Task<ProjectModerationResult> MoveToTrashAsync(
        int projectId,
        string actorUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var trimmedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedReason))
        {
            return ProjectModerationResult.ValidationFailed("A reason is required to move a project to Trash.");
        }

        if (trimmedReason!.Length > 512)
        {
            return ProjectModerationResult.ValidationFailed("Reason must be 512 characters or fewer.");
        }

        var project = await LoadProjectAsync(projectId, cancellationToken);
        if (project is null)
        {
            return ProjectModerationResult.NotFound();
        }

        if (project.IsDeleted)
        {
            return ProjectModerationResult.Success();
        }

        project.IsDeleted = true;
        project.DeletedAt = _clock.UtcNow;
        project.DeletedByUserId = actorUserId;
        project.DeleteReason = trimmedReason;
        project.DeleteMethod = "Trash";
        project.DeleteApprovedByUserId = null;

        await WriteAuditAsync(project.Id, AuditActions.Trash, actorUserId, trimmedReason, metadata: null, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return ProjectModerationResult.Success();
    }

    public async Task<ProjectModerationResult> RestoreFromTrashAsync(
        int projectId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var project = await LoadProjectAsync(projectId, cancellationToken);
        if (project is null)
        {
            return ProjectModerationResult.NotFound();
        }

        if (!project.IsDeleted)
        {
            return ProjectModerationResult.Success();
        }

        project.IsDeleted = false;
        project.DeletedAt = null;
        project.DeletedByUserId = null;
        project.DeleteReason = null;
        project.DeleteMethod = null;
        project.DeleteApprovedByUserId = null;

        await WriteAuditAsync(project.Id, AuditActions.RestoreTrash, actorUserId, reason: null, metadata: null, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return ProjectModerationResult.Success();
    }

    public async Task<ProjectModerationResult> PurgeAsync(
        int projectId,
        string actorUserId,
        bool includeAssets,
        CancellationToken cancellationToken = default)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return ProjectModerationResult.NotFound();
        }

        if (!project.IsDeleted)
        {
            return ProjectModerationResult.InvalidState("Project must be in Trash before it can be purged.");
        }

        using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var metadata = await PurgeProjectAsync(projectId, includeAssets, cancellationToken);

        await WriteAuditAsync(projectId, AuditActions.Purge, actorUserId, project.DeleteReason, metadata, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return ProjectModerationResult.Success();
    }

    public async Task<int> PurgeExpiredAsync(
        DateTimeOffset cutoffUtc,
        bool includeAssets,
        CancellationToken cancellationToken = default)
    {
        var projectIds = await _db.Projects
            .AsNoTracking()
            .Where(p => p.IsDeleted && p.DeletedAt != null && p.DeletedAt <= cutoffUtc)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var purged = 0;
        foreach (var projectId in projectIds)
        {
            try
            {
                var metadata = await PurgeProjectAsync(projectId, includeAssets, cancellationToken);
                await WriteAuditAsync(projectId, AuditActions.Purge, "system", reason: null, metadata, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                purged++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to purge project {ProjectId}", projectId);
            }
        }

        return purged;
    }

    private async Task<Project?> LoadProjectAsync(int projectId, CancellationToken cancellationToken)
    {
        return await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
    }

    private async Task<string?> PurgeProjectAsync(
        int projectId,
        bool includeAssets,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object?>();

        var photos = await _db.ProjectPhotos
            .Where(p => p.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["photoCount"] = photos.Count;
        _db.ProjectPhotos.RemoveRange(photos);

        var documents = await _db.ProjectDocuments
            .Where(d => d.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["documentCount"] = documents.Count;
        _db.ProjectDocuments.RemoveRange(documents);

        var comments = await _db.ProjectComments
            .Where(c => c.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["commentCount"] = comments.Count;
        _db.ProjectComments.RemoveRange(comments);

        var remarks = await _db.Remarks
            .Where(r => r.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["remarkCount"] = remarks.Count;
        _db.Remarks.RemoveRange(remarks);

        var tots = await _db.ProjectTots
            .Where(t => t.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["totCount"] = tots.Count;
        _db.ProjectTots.RemoveRange(tots);

        var stages = await _db.ProjectStages
            .Where(s => s.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["stageCount"] = stages.Count;
        _db.ProjectStages.RemoveRange(stages);

        var planSnapshots = await _db.ProjectPlanSnapshots
            .Where(s => s.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["planSnapshotCount"] = planSnapshots.Count;
        _db.ProjectPlanSnapshots.RemoveRange(planSnapshots);

        var snapshotIds = planSnapshots.Select(s => s.Id).ToList();
        if (snapshotIds.Count > 0)
        {
            var planSnapshotRows = await _db.ProjectPlanSnapshotRows
                .Where(s => snapshotIds.Contains(s.SnapshotId))
                .ToListAsync(cancellationToken);
            metadata["planSnapshotRowCount"] = planSnapshotRows.Count;
            _db.ProjectPlanSnapshotRows.RemoveRange(planSnapshotRows);
        }
        else
        {
            metadata["planSnapshotRowCount"] = 0;
        }

        var planVersions = await _db.PlanVersions
            .Where(v => v.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["planVersionCount"] = planVersions.Count;
        _db.PlanVersions.RemoveRange(planVersions);

        var planVersionIds = planVersions.Select(v => v.Id).ToList();
        if (planVersionIds.Count > 0)
        {
            var schedules = await _db.StagePlans
                .Where(p => planVersionIds.Contains(p.PlanVersionId))
                .ToListAsync(cancellationToken);
            metadata["stagePlanCount"] = schedules.Count;
            _db.StagePlans.RemoveRange(schedules);

            var approvals = await _db.PlanApprovalLogs
                .Where(p => planVersionIds.Contains(p.PlanVersionId))
                .ToListAsync(cancellationToken);
            metadata["planApprovalLogCount"] = approvals.Count;
            _db.PlanApprovalLogs.RemoveRange(approvals);
        }
        else
        {
            metadata["stagePlanCount"] = 0;
            metadata["planApprovalLogCount"] = 0;
        }

        var timelineEntries = await _db.StageChangeLogs
            .Where(l => l.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["timelineChangeLogCount"] = timelineEntries.Count;
        _db.StageChangeLogs.RemoveRange(timelineEntries);

        var metaRequests = await _db.ProjectMetaChangeRequests
            .Where(r => r.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["metaChangeRequestCount"] = metaRequests.Count;
        _db.ProjectMetaChangeRequests.RemoveRange(metaRequests);

        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project != null)
        {
            _db.Projects.Remove(project);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var assetsPurged = includeAssets && TryDeleteProjectAssets(projectId);
        metadata["assetsPurged"] = assetsPurged;

        return metadata.Count == 0
            ? null
            : JsonSerializer.Serialize(metadata);
    }

    private bool TryDeleteProjectAssets(int projectId)
    {
        try
        {
            var path = BuildProjectRootPath(projectId);
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return false;
            }

            Directory.Delete(path, recursive: true);
            TryDeleteParentIfEmpty(path);
            return true;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to delete asset directory for project {ProjectId}", projectId);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied deleting asset directory for project {ProjectId}", projectId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error deleting asset directory for project {ProjectId}", projectId);
            return false;
        }
    }

    private string BuildProjectRootPath(int projectId)
    {
        var projectSegment = projectId.ToString(CultureInfo.InvariantCulture);
        var segments = new[]
        {
            _uploadRootProvider.RootPath,
            string.IsNullOrWhiteSpace(_documentOptions.ProjectsSubpath) ? null : _documentOptions.ProjectsSubpath,
            projectSegment
        };

        return Path.GetFullPath(Path.Combine(segments.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()));
    }

    private void TryDeleteParentIfEmpty(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            var root = Path.GetFullPath(_uploadRootProvider.RootPath);
            while (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                var current = Path.GetFullPath(directory);
                if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    break;
                }

                Directory.Delete(directory, recursive: false);
                directory = Path.GetDirectoryName(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Failed to clean up parent directories after purging assets at {Path}", path);
        }
    }

    private async Task WriteAuditAsync(
        int projectId,
        string action,
        string performedByUserId,
        string? reason,
        string? metadata,
        CancellationToken cancellationToken)
    {
        var entry = new ProjectAudit
        {
            ProjectId = projectId,
            Action = action,
            PerformedByUserId = performedByUserId,
            PerformedAt = _clock.UtcNow,
            Reason = reason,
            MetadataJson = metadata
        };

        await _db.ProjectAudits.AddAsync(entry, cancellationToken);
    }
}
