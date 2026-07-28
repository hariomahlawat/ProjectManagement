using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Utilities;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Stages;

/// <summary>
/// Adds or corrects evidence-backed stage outcomes for legacy projects whose
/// lifecycle is already terminal. It deliberately writes the standard
/// ProjectStage aggregate so all existing timeline, repository and reporting
/// readers immediately consume the same authoritative data.
/// </summary>
public sealed class HistoricalStageRecordService
{
    private const string StageLogAction = "Backfill";
    private const string PendingDecisionStatus = "Pending";

    private readonly ApplicationDbContext _db;
    private readonly IWorkflowStageMetadataProvider _metadataProvider;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<HistoricalStageRecordService> _logger;
    private readonly IArppIpaStageAuthorityService _arppIpaStageAuthority;

    public HistoricalStageRecordService(
        ApplicationDbContext db,
        IWorkflowStageMetadataProvider metadataProvider,
        IClock clock,
        IAuditService audit,
        ILogger<HistoricalStageRecordService> logger,
        IArppIpaStageAuthorityService? arppIpaStageAuthority = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _metadataProvider = metadataProvider ?? throw new ArgumentNullException(nameof(metadataProvider));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _arppIpaStageAuthority = arppIpaStageAuthority ?? new ArppIpaStageAuthorityService(db);
    }

    public async Task<HistoricalStageEditorVm> GetEditorAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var project = EnsureEligible(
            await _db.Projects
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == projectId, cancellationToken),
            projectId);

        var stages = await _db.ProjectStages
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        var stageLookup = stages
            .Where(item => !string.IsNullOrWhiteSpace(item.StageCode))
            .ToDictionary(item => item.StageCode, StringComparer.OrdinalIgnoreCase);

        var ipaAuthority = await _arppIpaStageAuthority.ResolveAsync(projectId, cancellationToken);
        var definitions = _metadataProvider.GetStages(project.WorkflowVersion);
        var rows = definitions
            .Select((definition, index) =>
            {
                stageLookup.TryGetValue(definition.Code, out var stage);
                var hasRecordedData = stage is not null &&
                    (stage.Status != StageStatus.NotStarted ||
                     stage.ActualStart.HasValue ||
                     stage.CompletedOn.HasValue);

                return new HistoricalStageEditorRowVm
                {
                    StageCode = definition.Code,
                    StageName = definition.Name,
                    SortOrder = index,
                    ExistingStatus = stage?.Status ?? StageStatus.NotStarted,
                    Outcome = ResolveOutcome(project.LifecycleStatus, stage),
                    ActualStart = stage?.ActualStart,
                    CompletedOn = stage?.CompletedOn,
                    HasRecordedData = hasRecordedData,
                    IsArppManaged = ipaAuthority is not null &&
                        string.Equals(definition.Code, StageCodes.IPA, StringComparison.OrdinalIgnoreCase),
                    ArppSourceLabel = ipaAuthority is not null &&
                        string.Equals(definition.Code, StageCodes.IPA, StringComparison.OrdinalIgnoreCase)
                            ? $"{ipaAuthority.DocumentLabel} · {ipaAuthority.IssueDate:dd MMM yyyy}"
                            : null,
                    ArppCompletionDate = ipaAuthority is not null &&
                        string.Equals(definition.Code, StageCodes.IPA, StringComparison.OrdinalIgnoreCase)
                            ? ipaAuthority.IssueDate
                            : null
                };
            })
            .ToArray();
        var today = TodayInIndia();
        var terminalDate = ResolveTerminalDateUpperBound(project);

        return new HistoricalStageEditorVm
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            LifecycleStatus = project.LifecycleStatus,
            IsLegacy = project.IsLegacy,
            IsDeleted = project.IsDeleted,
            LatestPermittedDate = terminalDate.HasValue && terminalDate.Value < today
                ? terminalDate.Value
                : today,
            Rows = rows
        };
    }

    public async Task<HistoricalStageRecordResult> SaveAsync(
        HistoricalStageRecordInput input,
        string userId,
        string? userName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.ProjectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input.ProjectId));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(userId));
        }

        var project = EnsureEligible(
            await _db.Projects
                .SingleOrDefaultAsync(item => item.Id == input.ProjectId, cancellationToken),
            input.ProjectId);

        var evidenceNote = input.EvidenceNote?.Trim() ?? string.Empty;
        var errors = new List<string>();
        if (evidenceNote.Length < 5)
        {
            errors.Add("Describe the documentary source used for this historical update.");
        }
        else if (evidenceNote.Length > HistoricalStageRecordInput.EvidenceNoteMaxLength)
        {
            errors.Add($"The evidence note cannot exceed {HistoricalStageRecordInput.EvidenceNoteMaxLength} characters.");
        }

        var definitions = _metadataProvider.GetStages(project.WorkflowVersion);
        var definitionLookup = definitions
            .Select((definition, index) => new HistoricalStageDefinition(definition.Code, definition.Name, index))
            .ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);

        var rows = NormalizeRows(input.Rows, definitionLookup, errors);
        var ipaAuthority = await _arppIpaStageAuthority.ResolveAsync(project.Id, cancellationToken);
        if (ipaAuthority is not null)
        {
            var managedIpaRow = rows.FirstOrDefault(row =>
                string.Equals(row.StageCode, StageCodes.IPA, StringComparison.OrdinalIgnoreCase));

            if (managedIpaRow is null ||
                managedIpaRow.Outcome != HistoricalStageOutcome.Completed ||
                managedIpaRow.CompletedOn != ipaAuthority.IssueDate)
            {
                errors.Add(ArppManagedIpaStageException.UserMessage);
            }
        }

        ValidateRows(
            project,
            rows,
            errors,
            ipaAuthority is null ? null : StageCodes.IPA);

        if (errors.Count > 0)
        {
            throw new HistoricalStageRecordValidationException(errors);
        }

        var stages = await _db.ProjectStages
            .Where(item => item.ProjectId == project.Id)
            .ToListAsync(cancellationToken);

        var stageLookup = stages
            .Where(item => !string.IsNullOrWhiteSpace(item.StageCode))
            .ToDictionary(item => item.StageCode, StringComparer.OrdinalIgnoreCase);

        var changes = new List<HistoricalStageChange>();
        foreach (var row in rows)
        {
            if (row.Outcome == HistoricalStageOutcome.NotRecorded)
            {
                // NotRecorded is intentionally non-destructive. Existing history
                // can only be corrected to another evidenced outcome, not erased.
                continue;
            }

            stageLookup.TryGetValue(row.StageCode, out var stage);
            var isArppManagedIpa = ipaAuthority is not null &&
                                   string.Equals(row.StageCode, StageCodes.IPA, StringComparison.OrdinalIgnoreCase);
            var proposedStatus = isArppManagedIpa
                ? StageStatus.Completed
                : ResolveStatus(row.Outcome, stage);
            var proposedStart = isArppManagedIpa
                ? row.ActualStart
                : row.Outcome is HistoricalStageOutcome.Completed or HistoricalStageOutcome.Ceased
                    ? row.ActualStart
                    : null;
            var proposedCompleted = isArppManagedIpa
                ? ipaAuthority!.IssueDate
                : row.Outcome == HistoricalStageOutcome.Completed
                    ? row.CompletedOn
                    : null;
            var proposedBackfill = !isArppManagedIpa &&
                                   row.Outcome == HistoricalStageOutcome.Completed &&
                                   !proposedCompleted.HasValue;

            var definition = definitionLookup[row.StageCode];
            var isChanged = stage is null ||
                            stage.Status != proposedStatus ||
                            stage.SortOrder != definition.SortOrder ||
                            stage.ActualStart != proposedStart ||
                            stage.CompletedOn != proposedCompleted ||
                            stage.RequiresBackfill != proposedBackfill ||
                            stage.IsAutoCompleted ||
                            stage.AutoCompletedFromCode is not null;

            if (!isChanged)
            {
                continue;
            }

            changes.Add(new HistoricalStageChange(
                stage,
                definition,
                proposedStatus,
                proposedStart,
                proposedCompleted,
                proposedBackfill));
        }

        if (changes.Count == 0)
        {
            return HistoricalStageRecordResult.NoChanges();
        }

        var changedCodes = changes
            .Select(change => change.Definition.Code)
            .ToArray();

        var lockedCodes = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(request =>
                request.ProjectId == project.Id &&
                request.DecisionStatus == PendingDecisionStatus &&
                changedCodes.Contains(request.StageCode))
            .Select(request => request.StageCode)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (lockedCodes.Length > 0)
        {
            throw new HistoricalStageRecordConflictException(lockedCodes);
        }

        var now = _clock.UtcNow;
        await using var transaction = await RelationalTransactionScope.CreateAsync(
            _db.Database,
            cancellationToken);

        foreach (var change in changes)
        {
            var stage = change.Stage;
            var fromStatus = stage?.Status.ToString();
            var fromStart = stage?.ActualStart;
            var fromCompleted = stage?.CompletedOn;

            if (stage is null)
            {
                stage = new ProjectStage
                {
                    ProjectId = project.Id,
                    StageCode = change.Definition.Code
                };
                await _db.ProjectStages.AddAsync(stage, cancellationToken);
            }

            stage.SortOrder = change.Definition.SortOrder;
            stage.Status = change.Status;
            stage.ActualStart = change.ActualStart;
            stage.CompletedOn = change.CompletedOn;
            stage.RequiresBackfill = change.RequiresBackfill;
            stage.IsAutoCompleted = false;
            stage.AutoCompletedFromCode = null;

            await _db.StageChangeLogs.AddAsync(
                new StageChangeLog
                {
                    ProjectId = project.Id,
                    StageCode = change.Definition.Code,
                    Action = StageLogAction,
                    FromStatus = fromStatus,
                    ToStatus = stage.Status.ToString(),
                    FromActualStart = fromStart,
                    ToActualStart = stage.ActualStart,
                    FromCompletedOn = fromCompleted,
                    ToCompletedOn = stage.CompletedOn,
                    UserId = userId,
                    At = now,
                    Note = $"Historical stage evidence: {evidenceNote}"
                },
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var auditData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProjectId"] = project.Id.ToString(CultureInfo.InvariantCulture),
            ["LifecycleStatus"] = project.LifecycleStatus.ToString(),
            ["UpdatedCount"] = changes.Count.ToString(CultureInfo.InvariantCulture),
            // AuditService intentionally redacts payload keys containing "code";
            // use a non-sensitive key so the affected workflow stages remain useful.
            ["Stages"] = string.Join(",", changedCodes),
            ["EvidenceNote"] = evidenceNote
        };
        var changedCodeSummary = string.Join(",", changedCodes);

        await _audit.LogAsync(
            "Projects.HistoricalStageHistoryUpdated",
            message: $"Historical stage history updated for project {project.Id}.",
            userId: userId,
            userName: userName,
            data: auditData);

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Historical stage history updated for legacy project {ProjectId}; stages={StageCodes}; user={UserId}",
            project.Id,
            changedCodeSummary,
            userId);

        return new HistoricalStageRecordResult(changes.Count, changedCodes);
    }

    private static IReadOnlyList<NormalizedHistoricalStageRow> NormalizeRows(
        IList<HistoricalStageRecordRowInput>? inputRows,
        IReadOnlyDictionary<string, HistoricalStageDefinition> definitions,
        ICollection<string> errors)
    {
        if (inputRows is null || inputRows.Count == 0)
        {
            errors.Add("At least one workflow stage is required.");
            return Array.Empty<NormalizedHistoricalStageRow>();
        }

        var rows = new List<NormalizedHistoricalStageRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inputRow in inputRows)
        {
            if (inputRow is null || string.IsNullOrWhiteSpace(inputRow.StageCode))
            {
                errors.Add("A submitted stage code is missing.");
                continue;
            }

            var stageCode = inputRow.StageCode.Trim();
            if (!definitions.TryGetValue(stageCode, out var definition))
            {
                errors.Add($"{stageCode}: This stage is not part of the project's mapped workflow.");
                continue;
            }

            if (!seen.Add(definition.Code))
            {
                errors.Add($"{definition.Name}: The stage was submitted more than once.");
                continue;
            }

            if (!Enum.IsDefined(typeof(HistoricalStageOutcome), inputRow.Outcome))
            {
                errors.Add($"{definition.Name}: Select a valid historical outcome.");
                continue;
            }

            rows.Add(new NormalizedHistoricalStageRow(
                definition.Code,
                definition.Name,
                definition.SortOrder,
                inputRow.Outcome,
                inputRow.ActualStart,
                inputRow.CompletedOn));
        }

        if (seen.Count != definitions.Count)
        {
            errors.Add(
                "The submitted workflow stage list is incomplete. Reload the project before saving historical data.");
        }

        return rows;
    }

    private void ValidateRows(
        Project project,
        IReadOnlyList<NormalizedHistoricalStageRow> rows,
        ICollection<string> errors,
        string? externallyManagedCompletedStageCode = null)
    {
        var today = TodayInIndia();
        var terminalDate = ResolveTerminalDateUpperBound(project);
        var ceasedRows = rows
            .Where(row => row.Outcome == HistoricalStageOutcome.Ceased)
            .OrderBy(row => row.SortOrder)
            .ToArray();

        if (ceasedRows.Length > 1)
        {
            errors.Add("Only one workflow stage can be recorded as ceased at cancellation.");
        }

        if (ceasedRows.Length == 1)
        {
            var ceasedStage = ceasedRows[0];
            var laterResolvedStage = rows.FirstOrDefault(row =>
                row.SortOrder > ceasedStage.SortOrder &&
                (row.Outcome is HistoricalStageOutcome.Completed or HistoricalStageOutcome.Skipped) &&
                !string.Equals(
                    row.StageCode,
                    externallyManagedCompletedStageCode,
                    StringComparison.OrdinalIgnoreCase));
            if (laterResolvedStage is not null)
            {
                errors.Add(
                    $"{laterResolvedStage.StageName}: A stage after {ceasedStage.StageName} cannot be resolved when {ceasedStage.StageName} ceased at cancellation.");
            }
        }

        foreach (var row in rows)
        {
            if (row.ActualStart > today)
            {
                errors.Add($"{row.StageName}: Start date cannot be in the future.");
            }

            if (row.CompletedOn > today)
            {
                errors.Add($"{row.StageName}: Completion date cannot be in the future.");
            }

            if (terminalDate.HasValue && row.ActualStart > terminalDate)
            {
                errors.Add($"{row.StageName}: Start date cannot be after the project lifecycle date.");
            }

            if (terminalDate.HasValue && row.CompletedOn > terminalDate)
            {
                errors.Add($"{row.StageName}: Completion date cannot be after the project lifecycle date.");
            }

            if (row.ActualStart.HasValue &&
                row.CompletedOn.HasValue &&
                row.ActualStart > row.CompletedOn)
            {
                errors.Add($"{row.StageName}: Completion date must be on or after the start date.");
            }

            switch (row.Outcome)
            {
                case HistoricalStageOutcome.NotRecorded:
                    if (row.ActualStart.HasValue || row.CompletedOn.HasValue)
                    {
                        errors.Add($"{row.StageName}: Select an outcome before entering historical dates.");
                    }
                    break;

                case HistoricalStageOutcome.Completed:
                    break;

                case HistoricalStageOutcome.Skipped:
                    if (row.ActualStart.HasValue || row.CompletedOn.HasValue)
                    {
                        errors.Add($"{row.StageName}: A skipped stage cannot have actual dates.");
                    }
                    break;

                case HistoricalStageOutcome.Ceased:
                    if (project.LifecycleStatus != ProjectLifecycleStatus.Cancelled)
                    {
                        errors.Add($"{row.StageName}: Ceased is only valid for a cancelled project.");
                    }

                    if (row.CompletedOn.HasValue)
                    {
                        errors.Add($"{row.StageName}: A ceased stage cannot have a completion date.");
                    }
                    break;
            }
        }
    }

    private static Project EnsureEligible(Project? project, int projectId)
    {
        if (project is null)
        {
            throw new HistoricalStageRecordNotFoundException(projectId);
        }

        if (project.IsDeleted ||
            !project.IsLegacy ||
            (project.LifecycleStatus is not ProjectLifecycleStatus.Completed and
                not ProjectLifecycleStatus.Cancelled))
        {
            throw new HistoricalStageRecordNotAllowedException(projectId);
        }

        return project;
    }

    private static HistoricalStageOutcome ResolveOutcome(
        ProjectLifecycleStatus lifecycleStatus,
        ProjectStage? stage)
    {
        if (stage is null)
        {
            return HistoricalStageOutcome.NotRecorded;
        }

        return stage.Status switch
        {
            StageStatus.Completed => HistoricalStageOutcome.Completed,
            StageStatus.Skipped => HistoricalStageOutcome.Skipped,
            StageStatus.InProgress or StageStatus.Blocked
                when lifecycleStatus == ProjectLifecycleStatus.Cancelled =>
                HistoricalStageOutcome.Ceased,
            _ => HistoricalStageOutcome.NotRecorded
        };
    }

    private static StageStatus ResolveStatus(
        HistoricalStageOutcome outcome,
        ProjectStage? existing)
        => outcome switch
        {
            HistoricalStageOutcome.Completed => StageStatus.Completed,
            HistoricalStageOutcome.Skipped => StageStatus.Skipped,
            HistoricalStageOutcome.Ceased when existing?.Status == StageStatus.Blocked =>
                StageStatus.Blocked,
            HistoricalStageOutcome.Ceased => StageStatus.InProgress,
            _ => StageStatus.NotStarted
        };

    private DateOnly TodayInIndia()
        => DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(_clock.UtcNow, TimeZoneHelper.GetIst()).Date);

    /// <summary>
    /// Resolves the latest defensible stage date from the lifecycle precision
    /// that is actually recorded. A month-only completion permits dates through
    /// that month; a year-only completion permits dates through that year.
    /// </summary>
    private static DateOnly? ResolveTerminalDateUpperBound(Project project)
    {
        if (project.LifecycleStatus == ProjectLifecycleStatus.Cancelled)
        {
            return project.CancelledOn;
        }

        if (project.LifecycleStatus != ProjectLifecycleStatus.Completed)
        {
            return null;
        }

        if (project.CompletedOn.HasValue)
        {
            return project.CompletedOn;
        }

        if (!project.CompletedYear.HasValue ||
            project.CompletedYear.Value is < 1 or > 9999)
        {
            return null;
        }

        var completedYear = project.CompletedYear.Value;
        var recordedMonth = project.CompletedMonth.GetValueOrDefault();
        var completedMonth = recordedMonth is >= 1 and <= 12
            ? recordedMonth
            : 12;

        return new DateOnly(
            completedYear,
            completedMonth,
            DateTime.DaysInMonth(completedYear, completedMonth));
    }
}

public sealed record HistoricalStageRecordResult(
    int UpdatedCount,
    IReadOnlyList<string> StageCodes)
{
    public static HistoricalStageRecordResult NoChanges() =>
        new(0, Array.Empty<string>());
}

internal sealed record HistoricalStageDefinition(
    string Code,
    string Name,
    int SortOrder);

internal sealed record NormalizedHistoricalStageRow(
    string StageCode,
    string StageName,
    int SortOrder,
    HistoricalStageOutcome Outcome,
    DateOnly? ActualStart,
    DateOnly? CompletedOn);

internal sealed record HistoricalStageChange(
    ProjectStage? Stage,
    HistoricalStageDefinition Definition,
    StageStatus Status,
    DateOnly? ActualStart,
    DateOnly? CompletedOn,
    bool RequiresBackfill);

public sealed class HistoricalStageRecordValidationException : Exception
{
    public HistoricalStageRecordValidationException(IReadOnlyList<string> errors)
        : base("Historical stage input failed validation.")
    {
        Errors = errors ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Errors { get; }
}

public sealed class HistoricalStageRecordConflictException : Exception
{
    public HistoricalStageRecordConflictException(IReadOnlyList<string> stageCodes)
        : base("One or more stages have a pending decision.")
    {
        StageCodes = stageCodes ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> StageCodes { get; }
}

public sealed class HistoricalStageRecordNotFoundException : Exception
{
    public HistoricalStageRecordNotFoundException(int projectId)
        : base($"Project {projectId} was not found.")
    {
        ProjectId = projectId;
    }

    public int ProjectId { get; }
}

public sealed class HistoricalStageRecordNotAllowedException : Exception
{
    public HistoricalStageRecordNotAllowedException(int projectId)
        : base("Historical stage entry is only available for legacy completed or cancelled projects.")
    {
        ProjectId = projectId;
    }

    public int ProjectId { get; }
}
