using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Api;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationAnalysisService
{
    private const int MaximumSelectedProjects = 100;

    private readonly ApplicationDbContext _db;
    private readonly ProliferationAggregateReadService _aggregateReadService;
    private readonly ProliferationAnalysisExcelBuilder _excelBuilder;
    private readonly ILogger<ProliferationAnalysisService> _logger;

    public ProliferationAnalysisService(
        ApplicationDbContext db,
        ProliferationAggregateReadService aggregateReadService,
        ProliferationAnalysisExcelBuilder excelBuilder,
        ILogger<ProliferationAnalysisService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _aggregateReadService = aggregateReadService ?? throw new ArgumentNullException(nameof(aggregateReadService));
        _excelBuilder = excelBuilder ?? throw new ArgumentNullException(nameof(excelBuilder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProliferationAnalysisResultDto> RunAsync(
        ProliferationAnalysisRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var scope = await ResolveScopeAsync(request, cancellationToken);
        var projectIds = scope.Projects.Select(x => x.Id).ToArray();
        var projectIdSet = projectIds.ToHashSet();

        IReadOnlyList<ProliferationAnalysisProjectRowDto> projectRows;
        int annualQuantity;
        int detailedQuantity;
        string calculationBasis;

        if (request.PeriodMode == ProliferationAnalysisPeriodMode.CustomDates)
        {
            projectRows = await BuildCustomDateProjectRowsAsync(
                request,
                scope,
                projectIds,
                cancellationToken);
            annualQuantity = 0;
            detailedQuantity = projectRows.Sum(x => x.TotalQuantity);
            calculationBasis =
                "Exact-date totals are based on approved detailed entries only. Annual quantities are not included because they do not contain an exact date.";
        }
        else
        {
            var aggregateRows = await _aggregateReadService.GetApprovedAggregatesAsync(
                projectId: null,
                cancellationToken);

            var filtered = aggregateRows
                .Where(x => projectIdSet.Contains(x.ProjectId))
                .Where(x => !request.Source.HasValue || x.Source == request.Source.Value)
                .Where(x => IsAggregateInPeriod(x.Year, request))
                .ToList();

            annualQuantity = filtered.Sum(x => x.AnnualQuantity);
            detailedQuantity = filtered.Sum(x => x.DetailedQuantity);
            projectRows = BuildProjectRowsFromAggregates(scope, filtered);
            calculationBasis =
                "Reported totals are calculated for each simulator, source and year using the configured proliferation counting rule.";
        }

        var sddTotal = projectRows.Sum(x => x.SddQuantity);
        var abwTotal = projectRows.Sum(x => x.Abw515Quantity);
        var total = checked(sddTotal + abwTotal);
        var positiveRows = projectRows.Where(x => x.TotalQuantity > 0).ToList();

        // Unit-level data is deliberately queried only on demand. A failure in the
        // supplementary unit query must never prevent an authoritative total report.
        IReadOnlyList<ProliferationAnalysisUnitRowDto> unitRows =
            Array.Empty<ProliferationAnalysisUnitRowDto>();
        var unitDataLoaded = false;
        var unitQuantity = 0;
        var receivingUnitCount = 0;
        var hasUnitBreakdown = detailedQuantity > 0;

        if (request.IncludeUnitBreakdown)
        {
            unitDataLoaded = true;

            if (hasUnitBreakdown)
            {
                var projectMap = scope.Projects.ToDictionary(x => x.Id);
                unitRows = await BuildUnitRowsAsync(
                    request,
                    projectIds,
                    projectMap,
                    cancellationToken);

                unitQuantity = unitRows.Sum(x => x.Quantity);
                receivingUnitCount = unitRows
                    .Select(x => x.UnitName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
            }
        }

        var summary = new ProliferationAnalysisSummaryDto
        {
            TotalProliferation = total,
            SddTotal = sddTotal,
            Abw515Total = abwTotal,
            ProjectCount = positiveRows.Count,
            TechnicalCategoryCount = positiveRows
                .Select(x => x.TechnicalCategory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            ReceivingUnitCount = receivingUnitCount,
            ApprovedAnnualQuantity = annualQuantity,
            ApprovedDetailedQuantity = detailedQuantity,
            UnitBreakdownQuantity = unitQuantity,
            HasUnitBreakdown = hasUnitBreakdown,
            UnitDataLoaded = unitDataLoaded
        };

        _logger.LogDebug(
            "Generated proliferation analysis. Scope: {Scope}; Period: {Period}; Projects: {ProjectCount}; Total: {Total}; UnitDataLoaded: {UnitDataLoaded}",
            request.Scope,
            request.PeriodMode,
            projectIds.Length,
            total,
            unitDataLoaded);

        return new ProliferationAnalysisResultDto
        {
            ScopeLabel = scope.Label,
            PeriodLabel = BuildPeriodLabel(request),
            SourceLabel = request.Source?.ToDisplayName() ?? "All sources",
            CalculationBasis = calculationBasis,
            CoverageMessage = BuildCoverageMessage(
                request,
                annualQuantity,
                detailedQuantity,
                unitQuantity,
                unitDataLoaded),
            Summary = summary,
            Projects = projectRows,
            Units = unitRows
        };
    }

    public async Task<(byte[] Content, string FileName)> ExportAsync(
        ProliferationAnalysisRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var exportRequest = new ProliferationAnalysisRequestDto
        {
            Scope = request.Scope,
            PeriodMode = request.PeriodMode,
            TechnicalCategoryId = request.TechnicalCategoryId,
            ProjectIds = request.ProjectIds?.ToArray() ?? Array.Empty<int>(),
            Year = request.Year,
            FromYear = request.FromYear,
            ToYear = request.ToYear,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            Source = request.Source,
            IncludeUnitBreakdown = true
        };

        var result = await RunAsync(exportRequest, cancellationToken);
        var content = _excelBuilder.Build(result);
        var fileName = $"proliferation-analysis-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx";
        return (content, fileName);
    }

    private static void ValidateRequest(ProliferationAnalysisRequestDto request)
    {
        if (!Enum.IsDefined(request.Scope))
        {
            throw new ProliferationAnalysisValidationException("Select a valid analysis scope.");
        }

        if (!Enum.IsDefined(request.PeriodMode))
        {
            throw new ProliferationAnalysisValidationException("Select a valid period.");
        }

        if (request.Scope == ProliferationAnalysisScope.TechnicalCategory
            && (!request.TechnicalCategoryId.HasValue || request.TechnicalCategoryId.Value <= 0))
        {
            throw new ProliferationAnalysisValidationException("Select a technical category.");
        }

        var selectedIds = (request.ProjectIds ?? Array.Empty<int>())
            .Where(x => x > 0)
            .Distinct()
            .ToArray();

        if (request.Scope == ProliferationAnalysisScope.SelectedProjects && selectedIds.Length == 0)
        {
            throw new ProliferationAnalysisValidationException("Select at least one simulator.");
        }

        if (selectedIds.Length > MaximumSelectedProjects)
        {
            throw new ProliferationAnalysisValidationException($"Select no more than {MaximumSelectedProjects} simulators.");
        }

        var now = DateTimeOffset.UtcNow;
        var minimumYear = ProliferationYearPolicy.MinimumYear;
        var maximumYear = ProliferationYearPolicy.GetMaximumYear(now);

        switch (request.PeriodMode)
        {
            case ProliferationAnalysisPeriodMode.AllTime:
                break;

            case ProliferationAnalysisPeriodMode.SingleYear:
                if (!request.Year.HasValue || !ProliferationYearPolicy.IsValid(request.Year.Value, now))
                {
                    throw new ProliferationAnalysisValidationException($"Select a year between {minimumYear} and {maximumYear}.");
                }
                break;

            case ProliferationAnalysisPeriodMode.YearRange:
                if (!request.FromYear.HasValue || !request.ToYear.HasValue)
                {
                    throw new ProliferationAnalysisValidationException("Select both the first and last year.");
                }

                if (!ProliferationYearPolicy.IsValid(request.FromYear.Value, now)
                    || !ProliferationYearPolicy.IsValid(request.ToYear.Value, now))
                {
                    throw new ProliferationAnalysisValidationException($"Select years between {minimumYear} and {maximumYear}.");
                }

                if (request.FromYear.Value > request.ToYear.Value)
                {
                    throw new ProliferationAnalysisValidationException("The first year must be before or equal to the last year.");
                }
                break;

            case ProliferationAnalysisPeriodMode.CustomDates:
                if (!request.FromDate.HasValue || !request.ToDate.HasValue)
                {
                    throw new ProliferationAnalysisValidationException("Select both the start and end date.");
                }

                if (request.FromDate.Value > request.ToDate.Value)
                {
                    throw new ProliferationAnalysisValidationException("The start date must be before or equal to the end date.");
                }

                var minimumDate = new DateOnly(minimumYear, 1, 1);
                var maximumDate = new DateOnly(maximumYear, 12, 31);
                if (request.FromDate.Value < minimumDate || request.ToDate.Value > maximumDate)
                {
                    throw new ProliferationAnalysisValidationException(
                        $"Select dates between {minimumDate:dd MMM yyyy} and {maximumDate:dd MMM yyyy}.");
                }
                break;
        }
    }

    private async Task<ResolvedScope> ResolveScopeAsync(
        ProliferationAnalysisRequestDto request,
        CancellationToken cancellationToken)
    {
        var query = _db.Projects
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted
                && !x.IsArchived
                && x.LifecycleStatus == ProjectLifecycleStatus.Completed);

        string label;

        switch (request.Scope)
        {
            case ProliferationAnalysisScope.TechnicalCategory:
                var categoryId = request.TechnicalCategoryId!.Value;
                var categoryName = await _db.TechnicalCategories
                    .AsNoTracking()
                    .Where(x => x.Id == categoryId)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync(cancellationToken);

                if (string.IsNullOrWhiteSpace(categoryName))
                {
                    throw new ProliferationAnalysisValidationException("The selected technical category is not available.");
                }

                query = query.Where(x => x.TechnicalCategoryId == categoryId);
                label = $"Technical category: {categoryName}";
                break;

            case ProliferationAnalysisScope.SelectedProjects:
                var selectedIds = (request.ProjectIds ?? Array.Empty<int>())
                    .Where(x => x > 0)
                    .Distinct()
                    .ToArray();
                query = query.Where(x => selectedIds.Contains(x.Id));
                label = selectedIds.Length == 1
                    ? "Selected simulator"
                    : $"Selected simulators ({selectedIds.Length})";
                break;

            default:
                label = "All proliferation";
                break;
        }

        // Order while the query still targets mapped entity properties. EF Core cannot
        // translate ordering by a member of the projected ProjectInfo constructor.
        var projects = await query
            .OrderBy(x => x.Name)
            .Select(x => new ProjectInfo(
                x.Id,
                x.Name,
                x.CaseFileNumber,
                x.TechnicalCategoryId,
                x.TechnicalCategory != null ? x.TechnicalCategory.Name : "Not categorised"))
            .ToListAsync(cancellationToken);

        if (request.Scope == ProliferationAnalysisScope.SelectedProjects)
        {
            var expected = (request.ProjectIds ?? Array.Empty<int>()).Where(x => x > 0).Distinct().Count();
            if (projects.Count != expected)
            {
                throw new ProliferationAnalysisValidationException(
                    "One or more selected simulators are no longer eligible for proliferation reporting. Remove them and try again.");
            }
        }

        return new ResolvedScope(label, projects, request.Scope == ProliferationAnalysisScope.SelectedProjects);
    }

    private async Task<IReadOnlyList<ProliferationAnalysisUnitRowDto>> BuildUnitRowsAsync(
        ProliferationAnalysisRequestDto request,
        int[] projectIds,
        IReadOnlyDictionary<int, ProjectInfo> projectMap,
        CancellationToken cancellationToken)
    {
        if (projectIds.Length == 0)
        {
            return Array.Empty<ProliferationAnalysisUnitRowDto>();
        }

        var query = _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(x =>
                x.ApprovalStatus == ApprovalStatus.Approved
                && projectIds.Contains(x.ProjectId));

        if (request.Source.HasValue)
        {
            query = query.Where(x => x.Source == request.Source.Value);
        }

        query = ApplyDetailedPeriod(query, request);

        var entries = await query
            .Select(x => new UnitEntry(
                x.ProjectId,
                x.Source,
                x.UnitName,
                x.ProliferationDate,
                x.Quantity))
            .ToListAsync(cancellationToken);

        var grouped = entries
            .Where(x => !string.IsNullOrWhiteSpace(x.UnitName))
            .GroupBy(x => new
            {
                UnitKey = x.UnitName.Trim().ToUpperInvariant(),
                x.ProjectId,
                x.Source
            })
            .Select(group =>
            {
                var project = projectMap[group.Key.ProjectId];
                return new ProliferationAnalysisUnitRowDto
                {
                    UnitName = group.Select(x => x.UnitName.Trim()).First(),
                    ProjectId = project.Id,
                    ProjectName = project.Name,
                    ProjectCode = project.Code,
                    Source = group.Key.Source,
                    SourceLabel = group.Key.Source.ToDisplayName(),
                    Quantity = group.Sum(x => x.Quantity),
                    EntryCount = group.Count(),
                    FirstDate = group.Min(x => x.Date),
                    LastDate = group.Max(x => x.Date)
                };
            })
            .OrderByDescending(x => x.Quantity)
            .ThenBy(x => x.UnitName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return grouped;
    }

    private static IQueryable<ProliferationGranular> ApplyDetailedPeriod(
        IQueryable<ProliferationGranular> query,
        ProliferationAnalysisRequestDto request)
    {
        return request.PeriodMode switch
        {
            ProliferationAnalysisPeriodMode.SingleYear =>
                query.Where(x => x.ProliferationDate.Year == request.Year!.Value),

            ProliferationAnalysisPeriodMode.YearRange =>
                query.Where(x =>
                    x.ProliferationDate.Year >= request.FromYear!.Value
                    && x.ProliferationDate.Year <= request.ToYear!.Value),

            ProliferationAnalysisPeriodMode.CustomDates =>
                query.Where(x =>
                    x.ProliferationDate >= request.FromDate!.Value
                    && x.ProliferationDate <= request.ToDate!.Value),

            _ => query
        };
    }

    private static bool IsAggregateInPeriod(int year, ProliferationAnalysisRequestDto request)
    {
        return request.PeriodMode switch
        {
            ProliferationAnalysisPeriodMode.SingleYear => year == request.Year!.Value,
            ProliferationAnalysisPeriodMode.YearRange =>
                year >= request.FromYear!.Value && year <= request.ToYear!.Value,
            _ => true
        };
    }

    private static IReadOnlyList<ProliferationAnalysisProjectRowDto> BuildProjectRowsFromAggregates(
        ResolvedScope scope,
        IReadOnlyCollection<ProliferationAggregateRow> aggregates)
    {
        var quantityMap = aggregates
            .GroupBy(x => new { x.ProjectId, x.Source })
            .ToDictionary(
                group => (group.Key.ProjectId, group.Key.Source),
                group => group.Sum(x => x.ReportedTotal));

        return BuildProjectRows(scope, quantityMap);
    }

    private async Task<IReadOnlyList<ProliferationAnalysisProjectRowDto>> BuildCustomDateProjectRowsAsync(
        ProliferationAnalysisRequestDto request,
        ResolvedScope scope,
        int[] projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Length == 0)
        {
            return BuildProjectRows(
                scope,
                new Dictionary<(int ProjectId, ProliferationSource Source), int>());
        }

        var query = _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(x =>
                x.ApprovalStatus == ApprovalStatus.Approved
                && projectIds.Contains(x.ProjectId)
                && x.ProliferationDate >= request.FromDate!.Value
                && x.ProliferationDate <= request.ToDate!.Value);

        if (request.Source.HasValue)
        {
            query = query.Where(x => x.Source == request.Source.Value);
        }

        var totals = await query
            .GroupBy(x => new { x.ProjectId, x.Source })
            .Select(group => new
            {
                group.Key.ProjectId,
                group.Key.Source,
                Quantity = group.Sum(x => x.Quantity)
            })
            .ToListAsync(cancellationToken);

        var quantityMap = totals.ToDictionary(
            x => (x.ProjectId, x.Source),
            x => x.Quantity);

        return BuildProjectRows(scope, quantityMap);
    }

    private static IReadOnlyList<ProliferationAnalysisProjectRowDto> BuildProjectRows(
        ResolvedScope scope,
        IReadOnlyDictionary<(int ProjectId, ProliferationSource Source), int> quantityMap)
    {
        var rows = scope.Projects
            .Select(project =>
            {
                quantityMap.TryGetValue((project.Id, ProliferationSource.Sdd), out var sdd);
                quantityMap.TryGetValue((project.Id, ProliferationSource.Abw515), out var abw);
                return new ProliferationAnalysisProjectRowDto
                {
                    ProjectId = project.Id,
                    ProjectName = project.Name,
                    ProjectCode = project.Code,
                    TechnicalCategory = project.TechnicalCategoryName,
                    SddQuantity = sdd,
                    Abw515Quantity = abw,
                    TotalQuantity = checked(sdd + abw)
                };
            })
            .Where(x => scope.IncludeZeroRows || x.TotalQuantity > 0)
            .OrderByDescending(x => x.TotalQuantity)
            .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return rows;
    }

    private static string BuildPeriodLabel(ProliferationAnalysisRequestDto request)
    {
        return request.PeriodMode switch
        {
            ProliferationAnalysisPeriodMode.SingleYear => request.Year!.Value.ToString(),
            ProliferationAnalysisPeriodMode.YearRange =>
                request.FromYear == request.ToYear
                    ? request.FromYear!.Value.ToString()
                    : $"{request.FromYear} to {request.ToYear}",
            ProliferationAnalysisPeriodMode.CustomDates =>
                $"{request.FromDate!.Value:dd MMM yyyy} to {request.ToDate!.Value:dd MMM yyyy}",
            _ => "All time"
        };
    }

    private static string BuildCoverageMessage(
        ProliferationAnalysisRequestDto request,
        int annualQuantity,
        int detailedQuantity,
        int unitQuantity,
        bool unitDataLoaded)
    {
        if (!unitDataLoaded)
        {
            if (request.PeriodMode == ProliferationAnalysisPeriodMode.CustomDates)
            {
                return detailedQuantity > 0
                    ? $"This exact-date report contains {detailedQuantity:N0} approved detailed units. Load the unit-wise breakdown when receiving-unit names are required."
                    : "No approved detailed entries were recorded for the selected dates.";
            }

            if (annualQuantity > 0 && detailedQuantity > 0)
            {
                return $"Approved annual records in scope total {annualQuantity:N0} and do not contain receiving-unit names. "
                       + $"Approved detailed entries total {detailedQuantity:N0}; their unit-wise breakdown can be loaded when required. "
                       + "The reported total may differ because the configured counting rule determines which records contribute.";
            }

            if (annualQuantity > 0)
            {
                return $"Approved annual records in scope total {annualQuantity:N0}. Annual records do not contain receiving-unit names.";
            }

            if (detailedQuantity > 0)
            {
                return $"Approved detailed entries in scope total {detailedQuantity:N0}. Load the unit-wise breakdown when receiving-unit names are required.";
            }

            return "No approved proliferation was recorded for the selected scope and period.";
        }

        var detailedWithoutUnitName = Math.Max(0, detailedQuantity - unitQuantity);

        if (request.PeriodMode == ProliferationAnalysisPeriodMode.CustomDates)
        {
            if (detailedQuantity == 0)
            {
                return "No approved detailed entries were recorded for the selected dates.";
            }

            if (detailedWithoutUnitName > 0)
            {
                return $"Receiving-unit names are available for {unitQuantity:N0} of {detailedQuantity:N0} approved detailed units. "
                       + $"The remaining {detailedWithoutUnitName:N0} require receiving-unit data correction.";
            }

            return $"Unit names are available for all {unitQuantity:N0} approved detailed units in this exact-date report.";
        }

        if (annualQuantity > 0 && detailedQuantity > 0)
        {
            var missingUnitNote = detailedWithoutUnitName > 0
                ? $" {detailedWithoutUnitName:N0} detailed units do not have a usable receiving-unit name."
                : string.Empty;

            return $"Receiving-unit names are available from approved detailed entries totalling {unitQuantity:N0}. "
                   + $"Approved annual records in scope total {annualQuantity:N0} and do not contain receiving-unit names."
                   + missingUnitNote
                   + " The reported total may differ because the configured counting rule determines which records contribute.";
        }

        if (annualQuantity > 0)
        {
            return $"Approved annual records in scope total {annualQuantity:N0}. Annual records do not contain receiving-unit names.";
        }

        if (detailedQuantity > 0)
        {
            return detailedWithoutUnitName > 0
                ? $"Receiving-unit names are available for {unitQuantity:N0} of {detailedQuantity:N0} approved detailed units. "
                  + $"The remaining {detailedWithoutUnitName:N0} require receiving-unit data correction."
                : $"Receiving-unit names are available from approved detailed entries totalling {unitQuantity:N0}.";
        }

        return "No approved proliferation was recorded for the selected scope and period.";
    }

    private sealed record ResolvedScope(
        string Label,
        IReadOnlyList<ProjectInfo> Projects,
        bool IncludeZeroRows);

    private sealed record ProjectInfo(
        int Id,
        string Name,
        string? Code,
        int? TechnicalCategoryId,
        string TechnicalCategoryName);

    private sealed record UnitEntry(
        int ProjectId,
        ProliferationSource Source,
        string UnitName,
        DateOnly Date,
        int Quantity);
}
