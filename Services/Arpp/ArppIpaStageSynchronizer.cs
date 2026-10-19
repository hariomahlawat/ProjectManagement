using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Arpp;

/// <summary>
/// Applies the lifecycle consequence of a published ARPP position: the IPA stage
/// is completed on the first HQ-issued document date in which the linked project
/// appears. A published ARPP proves completion, but it does not prove when IPA
/// processing began; an unknown ActualStart therefore remains null.
/// </summary>
public sealed class ArppIpaStageSynchronizer : IArppIpaStageSynchronizer
{
    private const int SynchronizationBatchSize = 250;
    internal const string PendingRequestSupersededAction = "Superseded";
    private const string PendingDecisionStatus = "Pending";
    private const string SupersededDecisionStatus = "Superseded";
    private const string SystemUserId = "PRISM system";
    private const string SupersededRequestNote =
        "Superseded because published ARPP records became authoritative for In-Principle Approval.";

    private readonly ApplicationDbContext _db;
    private readonly IArppIpaStageAuthorityService _authority;
    private readonly IClock _clock;
    private readonly HashSet<string> _reportedDataQualityKeys = new(StringComparer.Ordinal);
    private bool _recordedDataQualityKeysLoaded;

    public ArppIpaStageSynchronizer(
        ApplicationDbContext db,
        IArppIpaStageAuthorityService? authority = null,
        IClock? clock = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _authority = authority ?? new ArppIpaStageAuthorityService(db);
        _clock = clock ?? new SystemClock();
    }

    public async Task<ArppIpaStageSynchronizationResult> SynchronizeAllAsync(
        CancellationToken cancellationToken = default)
    {
        var projectIds = await _db.ArppPublishedEntries
            .AsNoTracking()
            .Where(entry => entry.ProjectId.HasValue)
            .Select(entry => entry.ProjectId!.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (projectIds.Length == 0)
        {
            return ArppIpaStageSynchronizationResult.Empty;
        }

        var changes = new List<ArppIpaStageSynchronizationChange>();
        var dataQualityIssues = new List<ArppIpaStageDataQualityIssue>();
        var supersededRequestCount = 0;

        foreach (var batch in projectIds.Chunk(SynchronizationBatchSize))
        {
            var batchResult = await SynchronizeProjectsAsync(batch, cancellationToken);
            changes.AddRange(batchResult.Changes);
            dataQualityIssues.AddRange(batchResult.DataQualityIssues);
            supersededRequestCount += batchResult.SupersededRequestCount;
        }

        return new ArppIpaStageSynchronizationResult(
            projectIds.Length,
            changes,
            dataQualityIssues,
            supersededRequestCount);
    }

    public async Task<ArppIpaStageSynchronizationResult> SynchronizeProjectsAsync(
        IEnumerable<int> projectIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);

        var requestedIds = projectIds
            .Where(projectId => projectId > 0)
            .Distinct()
            .ToArray();

        if (requestedIds.Length == 0)
        {
            return ArppIpaStageSynchronizationResult.Empty;
        }

        // One batch is one unit of work: inside an ARPP verify/reconcile
        // transaction this is a savepoint; at startup it is its own transaction.
        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);

        var authorities = await _authority.ResolveManyAsync(requestedIds, cancellationToken);
        if (authorities.Count == 0)
        {
            return new ArppIpaStageSynchronizationResult(requestedIds.Length, [], [], 0);
        }

        var authoritativeProjectIds = authorities.Keys.ToArray();
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project => authoritativeProjectIds.Contains(project.Id) && !project.IsDeleted)
            .Select(project => new
            {
                project.Id,
                project.WorkflowVersion
            })
            .ToListAsync(cancellationToken);

        var existingStages = await _db.ProjectStages
            .Where(stage =>
                authoritativeProjectIds.Contains(stage.ProjectId) &&
                stage.StageCode == StageCodes.IPA)
            .ToDictionaryAsync(stage => stage.ProjectId, cancellationToken);

        var pendingRequests = await _db.StageChangeRequests
            .Where(request =>
                authoritativeProjectIds.Contains(request.ProjectId) &&
                request.StageCode == StageCodes.IPA &&
                request.DecisionStatus == PendingDecisionStatus)
            .ToListAsync(cancellationToken);

        var pendingRequestsByProject = pendingRequests
            .GroupBy(request => request.ProjectId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var recordedIssueKeys = await LoadRecordedDataQualityKeysAsync(cancellationToken);

        var changes = new List<ArppIpaStageSynchronizationChange>();
        var dataQualityIssues = new List<ArppIpaStageDataQualityIssue>();
        var supersededRequestCount = 0;
        var supersedeCandidates = new List<(ProjectStage Stage, StageChangeRequest Request)>();

        foreach (var project in projects)
        {
            var authority = authorities[project.Id];
            var completionDate = authority.IssueDate;
            var stageCreated = !existingStages.TryGetValue(project.Id, out var stage);

            if (stageCreated)
            {
                stage = new ProjectStage
                {
                    ProjectId = project.Id,
                    StageCode = StageCodes.IPA,
                    SortOrder = ProcurementWorkflow.OrderOf(project.WorkflowVersion, StageCodes.IPA),
                    Status = StageStatus.NotStarted,
                    ActualStart = null
                };
                _db.ProjectStages.Add(stage);
                existingStages[project.Id] = stage;
            }

            var previousStatus = stage!.Status;
            var previousCompletedOn = stage.CompletedOn;
            var previousActualStart = stage.ActualStart;
            var previousIsAutoCompleted = stage.IsAutoCompleted;
            var previousAutoCompletedFromCode = stage.AutoCompletedFromCode;
            var previousRequiresBackfill = stage.RequiresBackfill;

            stage.Status = StageStatus.Completed;
            stage.CompletedOn = completionDate;
            // Deliberately preserve a genuine recorded start and deliberately leave
            // an unknown start blank. ARPP publication proves completion only.
            stage.IsAutoCompleted = false;
            stage.AutoCompletedFromCode = null;
            stage.RequiresBackfill = false;

            if (stage.ActualStart is { } actualStart && actualStart > completionDate)
            {
                var issueKey = BuildDataQualityKey(project.Id, actualStart, completionDate);
                if (recordedIssueKeys.Add(issueKey))
                {
                    _reportedDataQualityKeys.Add(issueKey);
                    dataQualityIssues.Add(new ArppIpaStageDataQualityIssue(
                        project.Id,
                        actualStart,
                        completionDate,
                        authority.IssueId,
                        authority.DocumentLabel,
                        authority.IssueDate));
                }
            }

            if (pendingRequestsByProject.TryGetValue(project.Id, out var projectPendingRequests))
            {
                // Superseded after the stage rows are saved (see below), and only if
                // still pending then: a lifecycle decision may have claimed the
                // request since it was read.
                foreach (var request in projectPendingRequests)
                {
                    supersedeCandidates.Add((stage, request));
                }
            }

            var changed = stageCreated ||
                          previousStatus != stage.Status ||
                          previousCompletedOn != stage.CompletedOn ||
                          previousIsAutoCompleted != stage.IsAutoCompleted ||
                          !string.Equals(
                              previousAutoCompletedFromCode,
                              stage.AutoCompletedFromCode,
                              StringComparison.OrdinalIgnoreCase) ||
                          previousRequiresBackfill != stage.RequiresBackfill;

            if (!changed)
            {
                continue;
            }

            changes.Add(new ArppIpaStageSynchronizationChange(
                project.Id,
                completionDate,
                previousStatus,
                previousCompletedOn,
                previousActualStart,
                stageCreated,
                authority.IssueId,
                authority.DocumentLabel));
        }

        // Lock order matches lifecycle decisions: stage row first, request row second.
        if (changes.Count > 0 || dataQualityIssues.Count > 0 || supersedeCandidates.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        foreach (var (stage, request) in supersedeCandidates)
        {
            if (!await SupersedeIfStillPendingAsync(stage, request, cancellationToken))
            {
                continue;
            }

            supersededRequestCount++;
            await _db.StageChangeLogs.AddAsync(
                new StageChangeLog
                {
                    ProjectId = stage.ProjectId,
                    StageCode = StageCodes.IPA,
                    Action = PendingRequestSupersededAction,
                    FromStatus = stage.Status.ToString(),
                    ToStatus = stage.Status.ToString(),
                    FromActualStart = stage.ActualStart,
                    ToActualStart = stage.ActualStart,
                    FromCompletedOn = stage.CompletedOn,
                    ToCompletedOn = stage.CompletedOn,
                    UserId = SystemUserId,
                    At = _clock.UtcNow,
                    Note = SupersededRequestNote
                },
                cancellationToken);
        }

        if (supersededRequestCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new ArppIpaStageSynchronizationResult(
            requestedIds.Length,
            changes,
            dataQualityIssues,
            supersededRequestCount);
    }

    private async Task<bool> SupersedeIfStillPendingAsync(
        ProjectStage stage,
        StageChangeRequest request,
        CancellationToken cancellationToken)
    {
        var decidedOn = _clock.UtcNow;

        if (!_db.Database.IsRelational())
        {
            // EF's in-memory provider has no ExecuteUpdate; it runs no concurrent writers.
            request.DecisionStatus = SupersededDecisionStatus;
            request.DecidedByUserId = SystemUserId;
            request.DecidedOn = decidedOn;
            request.DecisionNote = SupersededRequestNote;
            return true;
        }

        var superseded = await _db.StageChangeRequests
            .Where(item => item.Id == request.Id && item.DecisionStatus == PendingDecisionStatus)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.DecisionStatus, SupersededDecisionStatus)
                    .SetProperty(item => item.DecidedByUserId, SystemUserId)
                    .SetProperty(item => item.DecidedOn, decidedOn)
                    .SetProperty(item => item.DecisionNote, SupersededRequestNote),
                cancellationToken);

        // Keep the tracked request and the history row in line with what is stored.
        await _db.Entry(request).ReloadAsync(cancellationToken);
        if (superseded != 1)
        {
            return false;
        }

        await _db.Entry(stage).ReloadAsync(cancellationToken);
        return true;
    }

    private async Task<HashSet<string>> LoadRecordedDataQualityKeysAsync(
        CancellationToken cancellationToken)
    {
        if (_recordedDataQualityKeysLoaded)
        {
            return new HashSet<string>(_reportedDataQualityKeys, StringComparer.Ordinal);
        }

        const string auditAction = "Arpp.IpaStageDataQualityIssue";

        var payloads = await _db.AuditLogs
            .AsNoTracking()
            .Where(log => log.Action == auditAction && log.DataJson != null)
            .Select(log => log.DataJson!)
            .ToListAsync(cancellationToken);

        foreach (var payload in payloads)
        {
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, string?>>(payload);
                if (data is null ||
                    !data.TryGetValue("ProjectId", out var projectIdText) ||
                    !int.TryParse(projectIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var projectId) ||
                    !data.TryGetValue("ActualStart", out var actualStartText) ||
                    !DateOnly.TryParseExact(
                        actualStartText,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var actualStart) ||
                    !data.TryGetValue("CompletionDate", out var completionDateText) ||
                    !DateOnly.TryParseExact(
                        completionDateText,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var completionDate))
                {
                    continue;
                }

                _reportedDataQualityKeys.Add(
                    BuildDataQualityKey(projectId, actualStart, completionDate));
            }
            catch (JsonException)
            {
                // A malformed legacy audit payload must not block ARPP synchronization.
            }
        }

        _recordedDataQualityKeysLoaded = true;
        return new HashSet<string>(_reportedDataQualityKeys, StringComparer.Ordinal);
    }

    private static string BuildDataQualityKey(
        int projectId,
        DateOnly actualStart,
        DateOnly completionDate)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{projectId}|{actualStart:yyyy-MM-dd}|{completionDate:yyyy-MM-dd}");
}

public sealed record ArppIpaStageSynchronizationResult(
    int EvaluatedProjectCount,
    IReadOnlyList<ArppIpaStageSynchronizationChange> Changes,
    IReadOnlyList<ArppIpaStageDataQualityIssue> DataQualityIssues,
    int SupersededRequestCount)
{
    public static ArppIpaStageSynchronizationResult Empty { get; } = new(0, [], [], 0);

    public int ChangedProjectCount => Changes.Count;

    public int DataQualityIssueCount => DataQualityIssues.Count;
}

public sealed record ArppIpaStageSynchronizationChange(
    int ProjectId,
    DateOnly CompletionDate,
    StageStatus PreviousStatus,
    DateOnly? PreviousCompletedOn,
    DateOnly? PreviousActualStart,
    bool StageCreated,
    long SourceIssueId,
    string SourceDocumentLabel);

public sealed record ArppIpaStageDataQualityIssue(
    int ProjectId,
    DateOnly ActualStart,
    DateOnly CompletionDate,
    long SourceIssueId,
    string SourceDocumentLabel,
    DateOnly SourceIssueDate);
