using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Remarks;

namespace ProjectManagement.Services.Reports.ArppFyProjectUpdate;

/// <summary>
/// Builds the FY-specific ARPP project update from the published ARPP current
/// position and live PRISM project facts. Membership is controlled by the
/// selected FY's published New/CL/CF position; lifecycle state controls only
/// ordering and applicability of current-stage fields.
/// </summary>
public sealed class ArppFyProjectUpdateService : IArppFyProjectUpdateService
{
    private readonly ApplicationDbContext _db;
    private readonly IArppLibraryService _arppLibrary;
    private readonly IArppIpaStageAuthorityService _ipaAuthority;
    private readonly IProjectFormalUpdateFactsResolver _formalFactsResolver;
    private readonly IProjectSupplyOrderValueResolver _supplyOrderValueResolver;
    private readonly IProjectLatestExternalRemarkService _externalRemarkService;
    private readonly IWorkflowStageMetadataProvider _workflowStages;
    private readonly IClock _clock;

    public ArppFyProjectUpdateService(
        ApplicationDbContext db,
        IArppLibraryService arppLibrary,
        IArppIpaStageAuthorityService ipaAuthority,
        IProjectFormalUpdateFactsResolver formalFactsResolver,
        IProjectSupplyOrderValueResolver supplyOrderValueResolver,
        IProjectLatestExternalRemarkService externalRemarkService,
        IWorkflowStageMetadataProvider workflowStages,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _arppLibrary = arppLibrary ?? throw new ArgumentNullException(nameof(arppLibrary));
        _ipaAuthority = ipaAuthority ?? throw new ArgumentNullException(nameof(ipaAuthority));
        _formalFactsResolver = formalFactsResolver ?? throw new ArgumentNullException(nameof(formalFactsResolver));
        _supplyOrderValueResolver = supplyOrderValueResolver ?? throw new ArgumentNullException(nameof(supplyOrderValueResolver));
        _externalRemarkService = externalRemarkService ?? throw new ArgumentNullException(nameof(externalRemarkService));
        _workflowStages = workflowStages ?? throw new ArgumentNullException(nameof(workflowStages));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<IReadOnlyList<int>> GetAvailableFinancialYearsAsync(
        CancellationToken cancellationToken = default)
    {
        var navigation = await _arppLibrary.GetNavigationAsync(null, cancellationToken);
        return navigation.FinancialYears
            .Select(year => year.FinancialYearStart)
            .Distinct()
            .OrderByDescending(year => year)
            .ToArray();
    }

    public async Task<ArppFyProjectUpdateReport?> BuildAsync(
        int financialYearStart,
        CancellationToken cancellationToken = default)
    {
        var currentPosition = await _arppLibrary.GetCurrentPositionAsync(
            financialYearStart,
            query: null,
            cancellationToken);
        if (currentPosition is null)
        {
            return null;
        }

        var approvedRows = currentPosition.ApprovedRows;
        var projectIds = approvedRows
            .Select(row => row.ProjectId)
            .Distinct()
            .ToArray();

        if (projectIds.Length == 0)
        {
            var noRowsWarnings = BuildGlobalWarnings(currentPosition.TotalUnlinkedDocumentRows);
            return new ArppFyProjectUpdateReport(
                financialYearStart,
                _clock.UtcNow.ToUniversalTime(),
                Array.Empty<ArppFyProjectUpdateRow>(),
                noRowsWarnings,
                currentPosition.TotalUnlinkedDocumentRows);
        }

        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id) && !project.IsDeleted)
            .Select(project => new ProjectSnapshot(
                project.Id,
                project.Name,
                project.LifecycleStatus,
                project.IsArchived,
                project.WorkflowVersion))
            .ToListAsync(cancellationToken);
        var projectById = projects.ToDictionary(project => project.Id);

        // Hard rule: completed projects are never included in the current-stage query.
        var stageEligibleIds = projects
            .Where(project => project.LifecycleStatus == ProjectLifecycleStatus.Active && !project.IsArchived)
            .Select(project => project.Id)
            .ToArray();

        var stageRows = stageEligibleIds.Length == 0
            ? new List<StageSnapshot>()
            : await _db.ProjectStages
                .AsNoTracking()
                .Where(stage => stageEligibleIds.Contains(stage.ProjectId))
                .Select(stage => new StageSnapshot(
                    stage.Id,
                    stage.ProjectId,
                    stage.StageCode,
                    stage.SortOrder,
                    stage.Status,
                    stage.PlannedStart,
                    stage.PlannedDue,
                    stage.ActualStart,
                    stage.CompletedOn))
                .ToListAsync(cancellationToken);
        var stagesByProject = stageRows
            .GroupBy(stage => stage.ProjectId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var firstListing = await _ipaAuthority.ResolveManyAsync(projectIds, cancellationToken);
        var formalFacts = await _formalFactsResolver.ResolveAsync(projectIds, cancellationToken);
        var supplyValues = await _supplyOrderValueResolver.ResolveAsync(projectIds, cancellationToken);
        var externalRemarks = await _externalRemarkService.GetLatestAsync(projectIds, cancellationToken);

        var warnings = BuildGlobalWarnings(currentPosition.TotalUnlinkedDocumentRows).ToList();
        var candidates = new List<RowCandidate>(approvedRows.Count);

        foreach (var arpp in approvedRows)
        {
            if (!projectById.TryGetValue(arpp.ProjectId, out var project))
            {
                warnings.Add(new ArppFyReportWarning(
                    "PROJECT_NOT_AVAILABLE",
                    ArppFyReportWarningSeverity.Warning,
                    $"{arpp.ProjectName}: the linked PRISM project is no longer available and was excluded from the report.",
                    arpp.ProjectId,
                    arpp.ProjectName));
                continue;
            }

            var stage = ResolveStage(project, stagesByProject.GetValueOrDefault(project.Id));
            firstListing.TryGetValue(project.Id, out var listing);
            formalFacts.TryGetValue(project.Id, out var facts);
            supplyValues.TryGetValue(project.Id, out var supplyValue);
            externalRemarks.TryGetValue(project.Id, out var externalRemark);
            supplyValue ??= ProjectSupplyOrderValue.Missing;

            var pdc = string.Equals(stage.Code, StageCodes.DEVP, StringComparison.OrdinalIgnoreCase)
                ? facts?.DevelopmentPdcDate
                : null;

            var candidate = new RowCandidate(
                project.Id,
                NormalizeNullable(arpp.PppNumber),
                project.Name,
                listing?.IssueDate,
                arpp.SourceIssueDate,
                NormalizeNullable(arpp.DfpdsSchedule),
                NormalizeNullable(arpp.Cfa),
                facts?.AonDate,
                supplyValue.AmountInRupees,
                supplyValue.Basis,
                facts?.SupplyOrderDate,
                pdc,
                arpp.Category,
                externalRemark?.Body,
                externalRemark?.EventDate,
                project.LifecycleStatus,
                project.IsArchived,
                stage.Code,
                stage.Label,
                stage.Order,
                stage.IsWorkflowConcludedWhileActive);

            candidates.Add(candidate);
            AddProjectWarnings(warnings, candidate);
        }

        var ordered = candidates
            .OrderBy(row => row.StageOrder)
            .ThenBy(row => row.PppNumber, PppNumberComparer.Instance)
            .ThenBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase)
            .Select((row, index) => new ArppFyProjectUpdateRow(
                index + 1,
                row.ProjectId,
                row.PppNumber,
                row.ProjectName,
                row.FirstArppListingDate,
                row.DfpdsSchedule,
                row.Cfa,
                "SDD",
                row.AonDate,
                row.SupplyOrderAmountInRupees,
                row.SupplyOrderAmountBasis,
                row.SupplyOrderDate,
                row.DevelopmentPdcDate,
                row.ProjectCase,
                row.LatestExternalRemark,
                row.LatestExternalRemarkEventDate,
                row.LifecycleStatus,
                row.IsArchived,
                row.CurrentStageCode,
                row.CurrentStageLabel,
                row.StageOrder)
            {
                CurrentFyArppListingDate = row.CurrentFyArppListingDate
            })
            .ToArray();

        return new ArppFyProjectUpdateReport(
            financialYearStart,
            _clock.UtcNow.ToUniversalTime(),
            ordered,
            warnings,
            currentPosition.TotalUnlinkedDocumentRows);
    }

    private ResolvedStage ResolveStage(ProjectSnapshot project, IReadOnlyList<StageSnapshot>? stageRows)
    {
        // Explicit completion is authoritative. Do not consult stage history.
        if (project.LifecycleStatus == ProjectLifecycleStatus.Completed)
        {
            return new ResolvedStage(
                ProjectStageMaturityOrder.CompletedCode,
                "Completed",
                ProjectStageMaturityOrder.Completed,
                false);
        }

        if (project.LifecycleStatus == ProjectLifecycleStatus.Cancelled)
        {
            return new ResolvedStage(null, "Cancelled", ProjectStageMaturityOrder.Unknown, false);
        }

        if (project.IsArchived)
        {
            return new ResolvedStage(null, "Archived", ProjectStageMaturityOrder.Unknown, false);
        }

        var projectForResolver = new Project
        {
            Id = project.Id,
            WorkflowVersion = project.WorkflowVersion,
            LifecycleStatus = project.LifecycleStatus,
            ProjectStages = (stageRows ?? Array.Empty<StageSnapshot>())
                .Select(stage => new ProjectStage
                {
                    Id = stage.Id,
                    ProjectId = stage.ProjectId,
                    StageCode = stage.StageCode,
                    SortOrder = stage.SortOrder,
                    Status = stage.Status,
                    PlannedStart = stage.PlannedStart,
                    PlannedDue = stage.PlannedDue,
                    ActualStart = stage.ActualStart,
                    CompletedOn = stage.CompletedOn
                })
                .ToList()
        };

        var lifecycleStages = ProjectLifecyclePositionResolver.BuildProjectStages(projectForResolver, _workflowStages);
        var position = ProjectLifecyclePositionResolver.Resolve(lifecycleStages);
        var resolved = position.CurrentStage ?? position.LastCompletedStage;

        if (resolved is null)
        {
            return new ResolvedStage(null, "Stage unresolved", ProjectStageMaturityOrder.Unknown, false);
        }

        return new ResolvedStage(
            resolved.Code,
            resolved.Name,
            ProjectStageMaturityOrder.Resolve(project.LifecycleStatus, resolved.Code),
            position.IsConcluded);
    }

    private static IReadOnlyList<ArppFyReportWarning> BuildGlobalWarnings(int unlinkedRows)
    {
        if (unlinkedRows <= 0)
        {
            return Array.Empty<ArppFyReportWarning>();
        }

        return new[]
        {
            new ArppFyReportWarning(
                "UNLINKED_ARPP_ROWS",
                ArppFyReportWarningSeverity.Warning,
                $"{unlinkedRows} published ARPP row{(unlinkedRows == 1 ? " is" : "s are")} not linked to a PRISM project and cannot be included in this report.")
        };
    }

    private static void AddProjectWarnings(List<ArppFyReportWarning> warnings, RowCandidate row)
    {
        void Add(string code, string message) => warnings.Add(new ArppFyReportWarning(
            code,
            ArppFyReportWarningSeverity.Warning,
            $"{row.ProjectName}: {message}",
            row.ProjectId,
            row.ProjectName));

        if (string.IsNullOrWhiteSpace(row.PppNumber))
        {
            Add("PPP_NUMBER_MISSING", "PPP Number / ARPP No. is not recorded in the current published FY position.");
        }

        if (!row.FirstArppListingDate.HasValue)
        {
            Add("FIRST_LISTING_DATE_MISSING", "the first published IPA/ARPP listing date could not be resolved.");
        }

        if (row.LifecycleStatus == ProjectLifecycleStatus.Cancelled)
        {
            Add("ARPP_LIFECYCLE_CANCELLED", "the current ARPP position is approved, but the PRISM project is marked Cancelled.");
        }
        else if (row.IsArchived && row.LifecycleStatus != ProjectLifecycleStatus.Completed)
        {
            Add("ARPP_LIFECYCLE_ARCHIVED", "the current ARPP position is approved, but the PRISM project is archived.");
        }

        if (row.IsWorkflowConcludedWhileActive)
        {
            Add("ACTIVE_WORKFLOW_CONCLUDED", "all procurement stages are terminal, but the project lifecycle is still Active.");
        }

        var isCompleted = row.LifecycleStatus == ProjectLifecycleStatus.Completed;

        // Being currently at AoN does not imply that AoN has been completed.
        // Warn only after the lifecycle has progressed beyond AoN (or the project
        // itself is explicitly Completed) and no valid completed-AoN milestone exists.
        var hasPassedAon = isCompleted
            || row.StageOrder < ProjectStageMaturityOrder.AcceptanceOfNecessity;
        if (hasPassedAon && !row.AonDate.HasValue)
        {
            Add("AON_DATE_MISSING", "AoN completion date is not recorded although the project has passed AoN.");
        }

        var hasReachedSupplyOrder = isCompleted || row.StageOrder <= ProjectStageMaturityOrder.SupplyOrder;
        if (hasReachedSupplyOrder)
        {
            if (row.SupplyOrderAmountInRupees is not > 0m && row.SupplyOrderDate.HasValue)
            {
                Add("SO_AMOUNT_MISSING", "Supply Order date is available, but neither a positive PNC cost nor L1 cost is available for SO amount.");
            }
            else if (row.SupplyOrderAmountInRupees is > 0m && !row.SupplyOrderDate.HasValue)
            {
                Add("SO_DATE_MISSING", "Supply Order amount can be resolved, but Supply Order date is not recorded.");
            }
            else if (row.SupplyOrderAmountInRupees is not > 0m && !row.SupplyOrderDate.HasValue)
            {
                Add("SO_DETAILS_MISSING", "Supply Order amount/date are not recorded although the project has reached or passed Supply Order.");
            }
        }

        if (string.Equals(row.CurrentStageCode, StageCodes.DEVP, StringComparison.OrdinalIgnoreCase)
            && !row.DevelopmentPdcDate.HasValue)
        {
            Add("DEVELOPMENT_PDC_MISSING", "current stage is Development, but Development PDC is not recorded.");
        }

        if (string.IsNullOrWhiteSpace(row.LatestExternalRemark))
        {
            Add("EXTERNAL_REMARK_MISSING", "no External / General project remark is available for the Remarks column.");
        }
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed record ProjectSnapshot(
        int Id,
        string Name,
        ProjectLifecycleStatus LifecycleStatus,
        bool IsArchived,
        string WorkflowVersion);

    private sealed record StageSnapshot(
        int Id,
        int ProjectId,
        string StageCode,
        int SortOrder,
        StageStatus Status,
        DateOnly? PlannedStart,
        DateOnly? PlannedDue,
        DateOnly? ActualStart,
        DateOnly? CompletedOn);

    private sealed record ResolvedStage(
        string? Code,
        string Label,
        int Order,
        bool IsWorkflowConcludedWhileActive);

    private sealed record RowCandidate(
        int ProjectId,
        string? PppNumber,
        string ProjectName,
        DateOnly? FirstArppListingDate,
        DateOnly CurrentFyArppListingDate,
        string? DfpdsSchedule,
        string? Cfa,
        DateOnly? AonDate,
        decimal? SupplyOrderAmountInRupees,
        ProjectSupplyOrderValueBasis SupplyOrderAmountBasis,
        DateOnly? SupplyOrderDate,
        DateOnly? DevelopmentPdcDate,
        ArppCategory ProjectCase,
        string? LatestExternalRemark,
        DateOnly? LatestExternalRemarkEventDate,
        ProjectLifecycleStatus LifecycleStatus,
        bool IsArchived,
        string? CurrentStageCode,
        string CurrentStageLabel,
        int StageOrder,
        bool IsWorkflowConcludedWhileActive);

    private sealed class PppNumberComparer : IComparer<string?>
    {
        public static PppNumberComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (string.IsNullOrWhiteSpace(x)) return 1;
            if (string.IsNullOrWhiteSpace(y)) return -1;

            var xParts = Regex.Split(x.Trim(), @"(\d+)");
            var yParts = Regex.Split(y.Trim(), @"(\d+)");
            var commonLength = Math.Min(xParts.Length, yParts.Length);

            for (var index = 0; index < commonLength; index++)
            {
                var xPart = xParts[index];
                var yPart = yParts[index];

                if (long.TryParse(xPart, out var xNumber)
                    && long.TryParse(yPart, out var yNumber))
                {
                    var numericComparison = xNumber.CompareTo(yNumber);
                    if (numericComparison != 0) return numericComparison;

                    // Keep otherwise equal values deterministic when one side uses
                    // leading zeroes (for example 7 versus 007).
                    var widthComparison = xPart.Length.CompareTo(yPart.Length);
                    if (widthComparison != 0) return widthComparison;
                    continue;
                }

                var textComparison = StringComparer.OrdinalIgnoreCase.Compare(xPart, yPart);
                if (textComparison != 0) return textComparison;
            }

            var lengthComparison = xParts.Length.CompareTo(yParts.Length);
            return lengthComparison != 0
                ? lengthComparison
                : StringComparer.OrdinalIgnoreCase.Compare(x, y);
        }
    }
}
