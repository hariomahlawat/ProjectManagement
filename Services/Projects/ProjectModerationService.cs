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
using ProjectManagement.Infrastructure;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
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

    private static class GlobalAuditActions
    {
        public const string ProjectPurge = "Projects.Purge";
    }

    // SECTION: Dependencies
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<ProjectModerationService> _logger;
    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly ProjectDocumentOptions _documentOptions;
    private readonly IAuditService _audit;
    private readonly ProjectRetentionOptions _retention;

    public const int MinimumPurgeReasonLength = 5;
    public const int MaximumPurgeReasonLength = 512;

    public ProjectModerationService(
        ApplicationDbContext db,
        IClock clock,
        ILogger<ProjectModerationService> logger,
        IUploadRootProvider uploadRootProvider,
        IOptions<ProjectDocumentOptions> documentOptions,
        IAuditService audit,
        IOptions<ProjectRetentionOptions> retention)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
        _documentOptions = documentOptions?.Value ?? throw new ArgumentNullException(nameof(documentOptions));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _retention = retention?.Value ?? throw new ArgumentNullException(nameof(retention));
    }

    // SECTION: Moderation actions
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

    /// <summary>
    /// Permanently deletes a project from Trash on an administrator's request. Every caller
    /// (the admin Trash page and the purge API) gets the same rules: a recorded reason, and
    /// the project must have been in Trash for the full retention period.
    /// </summary>
    public async Task<ProjectModerationResult> PurgeAsync(
        int projectId,
        string actorUserId,
        bool includeAssets,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var trimmedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedReason) || trimmedReason.Length < MinimumPurgeReasonLength)
        {
            return ProjectModerationResult.ValidationFailed("Record a clear administrative reason for permanent deletion.");
        }

        if (trimmedReason.Length > MaximumPurgeReasonLength)
        {
            return ProjectModerationResult.ValidationFailed(
                $"The permanent-deletion reason must be {MaximumPurgeReasonLength} characters or fewer.");
        }

        var project = await _db.Projects
            .AsNoTracking()
            .Where(candidate => candidate.Id == projectId)
            .Select(candidate => new PurgeCandidate(
                candidate.Id,
                candidate.Name,
                candidate.IsDeleted,
                candidate.DeletedAt,
                candidate.DeleteReason))
            .FirstOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return ProjectModerationResult.NotFound();
        }

        if (!project.IsDeleted)
        {
            return ProjectModerationResult.InvalidState("Project must be in Trash before it can be purged.");
        }

        if (project.DeletedAt is null)
        {
            return ProjectModerationResult.InvalidState(
                "This project has no recorded deletion date, so its retention period cannot be confirmed. Restore it and move it to Trash again to start the retention period.");
        }

        var purgeAvailableUtc = project.DeletedAt.Value.AddDays(_retention.EffectiveTrashRetentionDays);
        if (purgeAvailableUtc > _clock.UtcNow)
        {
            return ProjectModerationResult.InvalidState(
                "Permanent deletion is not available until the retention period has expired. Restore the project instead or wait.");
        }

        try
        {
            await PurgeCoreAsync(project, actorUserId, includeAssets, "Manual", trimmedReason, cancellationToken);
        }
        catch (ProjectNoLongerInTrashException)
        {
            return ProjectModerationResult.InvalidState("The project was restored or already purged by someone else. Nothing was deleted.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ProjectModerationResult.InvalidState("The project changed while it was being purged (for example, it was restored). Nothing was deleted.");
        }

        return ProjectModerationResult.Success();
    }

    public async Task<int> PurgeExpiredAsync(
        DateTimeOffset cutoffUtc,
        bool includeAssets,
        CancellationToken cancellationToken = default)
    {
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project => project.IsDeleted && project.DeletedAt != null && project.DeletedAt <= cutoffUtc)
            .Select(project => new PurgeCandidate(project.Id, project.Name, project.IsDeleted, project.DeletedAt, project.DeleteReason))
            .ToListAsync(cancellationToken);

        var purged = 0;
        foreach (var project in projects)
        {
            try
            {
                await PurgeCoreAsync(project, "system", includeAssets, "Expired", purgeReason: null, cancellationToken);
                purged++;
            }
            catch (ProjectNoLongerInTrashException)
            {
                _logger.LogInformation("Project {ProjectId} left Trash before it could be purged; skipped.", project.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to purge project {ProjectId}", project.Id);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        return purged;
    }

    // SECTION: Purge helpers
    private async Task<Project?> LoadProjectAsync(int projectId, CancellationToken cancellationToken)
    {
        return await _db.Projects.FirstOrDefaultAsync(project => project.Id == projectId, cancellationToken);
    }

    private async Task PurgeCoreAsync(
        PurgeCandidate project,
        string actorUserId,
        bool includeAssets,
        string source,
        string? purgeReason,
        CancellationToken cancellationToken)
    {
        FileQuarantineHandle? quarantinedAssets = null;
        var metadata = new Dictionary<string, object?>();

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);

        try
        {
            // The caller checked IsDeleted before this transaction. A restore that committed
            // since then must win: re-check inside the transaction, before any file moves.
            // A restore that commits after the project row is loaded below is caught by the
            // row version (DbUpdateConcurrencyException) and rolls the purge back.
            if (!await _db.Projects.IgnoreQueryFilters().AnyAsync(
                    candidate => candidate.Id == project.Id && candidate.IsDeleted,
                    cancellationToken))
            {
                throw new ProjectNoLongerInTrashException(project.Id);
            }

            if (includeAssets)
            {
                quarantinedAssets = FileSystemQuarantine.StageDirectory(
                    BuildProjectRootPath(project.Id),
                    _uploadRootProvider.RootPath,
                    "projects",
                    project.Id.ToString(CultureInfo.InvariantCulture));

                metadata["assetDisposition"] = quarantinedAssets is null ? "NotFound" : "Quarantined";
                if (quarantinedAssets is not null)
                {
                    metadata["assetQuarantineRef"] = FileSystemQuarantine.GetSafeReference(quarantinedAssets);
                }
            }
            else
            {
                metadata["assetDisposition"] = "Retained";
            }

            await PurgeProjectDatabaseRecordsAsync(project.Id, metadata, cancellationToken);
            var metadataJson = JsonSerializer.Serialize(metadata);

            await WritePurgeAuditAsync(
                project,
                actorUserId,
                includeAssets,
                metadataJson,
                source,
                purgeReason);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();

            if (quarantinedAssets is not null)
            {
                try
                {
                    FileSystemQuarantine.Restore(quarantinedAssets);
                }
                catch (Exception restoreError)
                {
                    _logger.LogCritical(
                        restoreError,
                        "Project {ProjectId} database purge failed and its asset quarantine could not be restored. Quarantine reference: {Reference}",
                        project.Id,
                        FileSystemQuarantine.GetSafeReference(quarantinedAssets));
                    throw new InvalidOperationException(
                        "The project purge failed and the asset directory could not be restored automatically.",
                        restoreError);
                }
            }

            throw;
        }

        if (quarantinedAssets is null)
        {
            return;
        }

        try
        {
            FileSystemQuarantine.FinalizeDeletion(quarantinedAssets);
            TryDeleteParentIfEmpty(quarantinedAssets.OriginalPath);
        }
        catch (Exception cleanupError)
        {
            var reference = FileSystemQuarantine.GetSafeReference(quarantinedAssets);
            _logger.LogError(
                cleanupError,
                "Project {ProjectId} was purged but final deletion of quarantined assets failed. Quarantine reference: {Reference}",
                project.Id,
                reference);

            try
            {
                await _audit.LogAsync(
                    "Projects.PurgeAssetCleanupPending",
                    message: $"Project {project.Id} database records were purged, but quarantined assets require cleanup.",
                    level: "Warning",
                    userId: actorUserId,
                    data: new Dictionary<string, string?>
                    {
                        ["ProjectId"] = project.Id.ToString(CultureInfo.InvariantCulture),
                        ["QuarantineReference"] = reference,
                        ["Source"] = source
                    });
            }
            catch (Exception auditError)
            {
                _logger.LogCritical(
                    auditError,
                    "Project {ProjectId} asset cleanup is pending and the warning audit could not be recorded. Quarantine reference: {Reference}",
                    project.Id,
                    reference);
            }
        }
    }

    private async Task PurgeProjectDatabaseRecordsAsync(
        int projectId,
        IDictionary<string, object?> metadata,
        CancellationToken cancellationToken)
    {
        // Actual-correction proposals (Phase D). Items restrict their revision and the
        // revisions reference each other, so the lineage links are cleared and saved first,
        // before any other delete is queued: that save flushes only the link updates. It
        // runs inside the purge transaction, so it is undone if the purge fails.
        var actualRevisions = await _db.ActualCorrectionRevisions
            .Where(revision => revision.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["actualCorrectionRevisionCount"] = actualRevisions.Count;
        if (actualRevisions.Count > 0)
        {
            foreach (var revision in actualRevisions)
            {
                revision.SupersedesRevisionId = null;
                revision.SupersededByRevisionId = null;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        var actualRevisionIds = actualRevisions.Select(revision => revision.Id).ToList();
        var actualItems = actualRevisionIds.Count == 0
            ? new List<ActualCorrectionItem>()
            : await _db.ActualCorrectionItems
                .Where(item => actualRevisionIds.Contains(item.RevisionId))
                .ToListAsync(cancellationToken);
        metadata["actualCorrectionItemCount"] = actualItems.Count;
        _db.ActualCorrectionItems.RemoveRange(actualItems);
        _db.ActualCorrectionRevisions.RemoveRange(actualRevisions);

        var photos = await _db.ProjectPhotos
            .Where(photo => photo.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["photoCount"] = photos.Count;
        _db.ProjectPhotos.RemoveRange(photos);

        var videos = await _db.ProjectVideos
            .Where(video => video.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["videoCount"] = videos.Count;
        _db.ProjectVideos.RemoveRange(videos);

        var documents = await _db.ProjectDocuments
            .Where(document => document.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["documentCount"] = documents.Count;
        _db.ProjectDocuments.RemoveRange(documents);

        var comments = await _db.ProjectComments
            .Where(comment => comment.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["commentCount"] = comments.Count;
        _db.ProjectComments.RemoveRange(comments);

        var remarks = await _db.Remarks
            .Where(remark => remark.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["remarkCount"] = remarks.Count;
        _db.Remarks.RemoveRange(remarks);

        var tots = await _db.ProjectTots
            .Where(tot => tot.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["totCount"] = tots.Count;
        _db.ProjectTots.RemoveRange(tots);

        // Proliferation rows have no foreign key to Projects, so a purge must remove them
        // itself or they linger as orphans (amendments go with their records).
        var proliferationYearlies = await _db.ProliferationYearlies
            .Where(record => record.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["proliferationYearlyCount"] = proliferationYearlies.Count;
        _db.ProliferationYearlies.RemoveRange(proliferationYearlies);

        var proliferationEntries = await _db.ProliferationGranularEntries
            .Where(entry => entry.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["proliferationEntryCount"] = proliferationEntries.Count;
        _db.ProliferationGranularEntries.RemoveRange(proliferationEntries);

        var proliferationPreferences = await _db.ProliferationYearPreferences
            .Where(preference => preference.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["proliferationCountingRuleCount"] = proliferationPreferences.Count;
        _db.ProliferationYearPreferences.RemoveRange(proliferationPreferences);

        var stages = await _db.ProjectStages
            .Where(stage => stage.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["stageCount"] = stages.Count;
        _db.ProjectStages.RemoveRange(stages);

        var planSnapshots = await _db.ProjectPlanSnapshots
            .Where(snapshot => snapshot.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["planSnapshotCount"] = planSnapshots.Count;
        _db.ProjectPlanSnapshots.RemoveRange(planSnapshots);

        var snapshotIds = planSnapshots.Select(snapshot => snapshot.Id).ToList();
        var snapshotRows = snapshotIds.Count == 0
            ? new List<ProjectPlanSnapshotRow>()
            : await _db.ProjectPlanSnapshotRows
                .Where(row => snapshotIds.Contains(row.SnapshotId))
                .ToListAsync(cancellationToken);
        metadata["planSnapshotRowCount"] = snapshotRows.Count;
        _db.ProjectPlanSnapshotRows.RemoveRange(snapshotRows);

        var planVersions = await _db.PlanVersions
            .Where(version => version.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["planVersionCount"] = planVersions.Count;
        _db.PlanVersions.RemoveRange(planVersions);

        var planVersionIds = planVersions.Select(version => version.Id).ToList();
        var stagePlans = planVersionIds.Count == 0
            ? new List<StagePlan>()
            : await _db.StagePlans
                .Where(plan => planVersionIds.Contains(plan.PlanVersionId))
                .ToListAsync(cancellationToken);
        metadata["stagePlanCount"] = stagePlans.Count;
        _db.StagePlans.RemoveRange(stagePlans);

        var approvalLogs = planVersionIds.Count == 0
            ? new List<PlanApprovalLog>()
            : await _db.PlanApprovalLogs
                .Where(log => planVersionIds.Contains(log.PlanVersionId))
                .ToListAsync(cancellationToken);
        metadata["planApprovalLogCount"] = approvalLogs.Count;
        _db.PlanApprovalLogs.RemoveRange(approvalLogs);

        var timelineEntries = await _db.StageChangeLogs
            .Where(log => log.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["timelineChangeLogCount"] = timelineEntries.Count;
        _db.StageChangeLogs.RemoveRange(timelineEntries);

        var metaRequests = await _db.ProjectMetaChangeRequests
            .Where(request => request.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["metaChangeRequestCount"] = metaRequests.Count;
        _db.ProjectMetaChangeRequests.RemoveRange(metaRequests);

        // Briefing-deck items restrict the project, so a purged project leaves every shared
        // deck it was in. Each such deck gets a new version so an open editor reloads instead
        // of saving an order or override for a project that no longer exists.
        var briefingDeckItems = await _db.ProjectBriefingDeckItems
            .Include(item => item.Deck)
            .Where(item => item.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        metadata["briefingDeckItemCount"] = briefingDeckItems.Count;
        foreach (var deck in briefingDeckItems.Select(item => item.Deck).Distinct())
        {
            deck.RowVersion = Guid.NewGuid().ToByteArray();
        }
        _db.ProjectBriefingDeckItems.RemoveRange(briefingDeckItems);

        var projectEntity = await _db.Projects
            .FirstOrDefaultAsync(candidate => candidate.Id == projectId, cancellationToken);
        if (projectEntity is null || !projectEntity.IsDeleted)
        {
            throw new ProjectNoLongerInTrashException(projectId);
        }

        _db.Projects.Remove(projectEntity);

        await _db.SaveChangesAsync(cancellationToken);
    }

    private sealed class ProjectNoLongerInTrashException : Exception
    {
        public ProjectNoLongerInTrashException(int projectId)
            : base($"Project {projectId} is no longer in Trash.")
        {
        }
    }

    private sealed record PurgeCandidate(
        int Id,
        string Name,
        bool IsDeleted,
        DateTimeOffset? DeletedAt,
        string? DeleteReason);

    // SECTION: Asset cleanup
    private string BuildProjectRootPath(int projectId)
    {
        var rootPath = _uploadRootProvider.RootPath;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException("Upload root path cannot be null or empty.");
        }

        var basePath = string.IsNullOrWhiteSpace(_documentOptions.ProjectsSubpath)
            ? rootPath
            : Path.Combine(rootPath, _documentOptions.ProjectsSubpath);

        var fullPath = Path.Combine(basePath, projectId.ToString(CultureInfo.InvariantCulture));

        return Path.GetFullPath(fullPath);
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

    // SECTION: Audit logging
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

    private async Task WritePurgeAuditAsync(
        PurgeCandidate project,
        string performedByUserId,
        bool includeAssets,
        string? metadata,
        string source,
        string? purgeReason)
    {
        var projectId = project.Id;
        // "Reason" stays the Trash reason, as before. The purge is recorded here, inside the
        // purge transaction, with the project name (the row is gone afterwards) and the
        // administrator's purge reason.
        var data = new Dictionary<string, string?>
        {
            ["ProjectId"] = projectId.ToString(CultureInfo.InvariantCulture),
            ["ProjectName"] = project.Name,
            ["IncludeAssets"] = includeAssets ? "true" : "false",
            ["Reason"] = project.DeleteReason,
            ["PurgeReason"] = purgeReason,
            ["Metadata"] = metadata,
            ["Source"] = source
        };

        await _audit.LogAsync(
            GlobalAuditActions.ProjectPurge,
            message: purgeReason is null
                ? $"Project '{project.Name}' ({projectId}) purged."
                : $"Project '{project.Name}' ({projectId}) purged. Reason: {purgeReason}",
            userId: performedByUserId,
            data: data);
    }
}
