using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;
using ProjectManagement.Models.Projects;

namespace ProjectManagement.Services.Projects;

// SECTION: Read model for the concise completed-project portfolio overview.
public sealed class CompletedProjectsPortfolioOverview
{
    public static CompletedProjectsPortfolioOverview Empty { get; } = Build(Array.Empty<CompletedProjectSummaryDto>(), DateTime.Today.Year);

    public int TotalCount { get; init; }
    public int AvailableCount { get; init; }
    public int NotAvailableCount { get; init; }
    public int AvailabilityPendingCount { get; init; }
    public int TechnologyCurrentCount { get; init; }
    public int TechnologyOutdatedCount { get; init; }
    public int TechnologyObsoleteCount { get; init; }
    public int TechnologyAssessmentPendingCount { get; init; }
    public int TechnologyActionCount => TechnologyOutdatedCount + TechnologyObsoleteCount;
    public int TotActionPendingCount { get; init; }
    public int TotCompletedCount { get; init; }
    public int CriticalIncompleteCount { get; init; }
    public int SupplementaryIncompleteCount { get; init; }
    public int FullyCompleteCount { get; init; }
    public decimal RecordedDevelopmentCostLakhs { get; init; }
    public int RecordedDevelopmentCostCount { get; init; }
    public int Age0To5Count { get; init; }
    public int Age6To10Count { get; init; }
    public int Age11To15Count { get; init; }
    public int Age16PlusCount { get; init; }
    public int AgeUnknownCount { get; init; }

    public IReadOnlyList<CompletedProjectSummaryDto> AvailableProjects { get; init; } = Array.Empty<CompletedProjectSummaryDto>();
    public IReadOnlyList<CompletedProjectSummaryDto> AvailabilityPendingProjects { get; init; } = Array.Empty<CompletedProjectSummaryDto>();
    public IReadOnlyList<CompletedProjectSummaryDto> TechnologyActionProjects { get; init; } = Array.Empty<CompletedProjectSummaryDto>();
    public IReadOnlyList<CompletedProjectSummaryDto> TotActionProjects { get; init; } = Array.Empty<CompletedProjectSummaryDto>();
    public IReadOnlyList<CompletedProjectSummaryDto> CriticalIncompleteProjects { get; init; } = Array.Empty<CompletedProjectSummaryDto>();
    public IReadOnlyList<CompletedProjectSummaryDto> SupplementaryOnlyProjects { get; init; } = Array.Empty<CompletedProjectSummaryDto>();

    public static CompletedProjectsPortfolioOverview Build(
        IReadOnlyList<CompletedProjectSummaryDto> items,
        int currentYear,
        int queueSize = 5)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (queueSize < 1) throw new ArgumentOutOfRangeException(nameof(queueSize));

        var available = items.Where(x => x.AvailableForProliferation == true).ToList();
        var availabilityPending = items.Where(CompletedProjectPortfolioPolicy.IsProliferationAssessmentPending).ToList();
        var technologyAction = items.Where(CompletedProjectPortfolioPolicy.RequiresTechnologyAction).ToList();
        var totAction = items.Where(CompletedProjectPortfolioPolicy.HasTotActionPending).ToList();
        var criticalIncomplete = items
            .Where(x => CompletedProjectPortfolioPolicy.GetCriticalMissingCount(x) > 0)
            .ToList();
        var supplementaryIncomplete = items
            .Where(x => CompletedProjectPortfolioPolicy.GetSupplementaryMissingCount(x) > 0)
            .ToList();
        var supplementaryOnly = items
            .Where(x => CompletedProjectPortfolioPolicy.GetCriticalMissingCount(x) == 0
                        && CompletedProjectPortfolioPolicy.GetSupplementaryMissingCount(x) > 0)
            .ToList();

        return new CompletedProjectsPortfolioOverview
        {
            TotalCount = items.Count,
            AvailableCount = available.Count,
            NotAvailableCount = items.Count(x => x.AvailableForProliferation == false),
            AvailabilityPendingCount = availabilityPending.Count,
            TechnologyCurrentCount = items.Count(x => string.Equals(x.TechStatus, ProjectTechStatusCodes.Current, StringComparison.OrdinalIgnoreCase)),
            TechnologyOutdatedCount = items.Count(x => string.Equals(x.TechStatus, ProjectTechStatusCodes.Outdated, StringComparison.OrdinalIgnoreCase)),
            TechnologyObsoleteCount = items.Count(x => string.Equals(x.TechStatus, ProjectTechStatusCodes.Obsolete, StringComparison.OrdinalIgnoreCase)),
            TechnologyAssessmentPendingCount = items.Count(CompletedProjectPortfolioPolicy.IsTechnologyAssessmentPending),
            TotActionPendingCount = totAction.Count,
            TotCompletedCount = items.Count(x => !x.IsBuild && x.TotStatus == ProjectTotStatus.Completed),
            CriticalIncompleteCount = criticalIncomplete.Count,
            SupplementaryIncompleteCount = supplementaryIncomplete.Count,
            FullyCompleteCount = items.Count(x => CompletedProjectPortfolioPolicy.GetTotalMissingCount(x) == 0),
            RecordedDevelopmentCostLakhs = items.Where(x => x.RdCostLakhs.HasValue).Sum(x => x.RdCostLakhs ?? 0m),
            RecordedDevelopmentCostCount = items.Count(x => x.RdCostLakhs.HasValue),
            Age0To5Count = items.Count(x => AgeInRange(x.CompletedYear, currentYear, 0, 5)),
            Age6To10Count = items.Count(x => AgeInRange(x.CompletedYear, currentYear, 6, 10)),
            Age11To15Count = items.Count(x => AgeInRange(x.CompletedYear, currentYear, 11, 15)),
            Age16PlusCount = items.Count(x => x.CompletedYear.HasValue && currentYear - x.CompletedYear.Value >= 16),
            AgeUnknownCount = items.Count(x => !x.CompletedYear.HasValue),
            AvailableProjects = Prioritise(available, queueSize),
            AvailabilityPendingProjects = Prioritise(availabilityPending, queueSize),
            TechnologyActionProjects = Prioritise(technologyAction, queueSize),
            TotActionProjects = Prioritise(totAction, queueSize),
            CriticalIncompleteProjects = criticalIncomplete
                .OrderByDescending(CompletedProjectPortfolioPolicy.GetCriticalMissingCount)
                .ThenByDescending(x => x.CompletedYear ?? int.MinValue)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Take(queueSize)
                .ToList(),
            SupplementaryOnlyProjects = supplementaryOnly
                .OrderByDescending(CompletedProjectPortfolioPolicy.GetSupplementaryMissingCount)
                .ThenByDescending(x => x.CompletedYear ?? int.MinValue)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Take(queueSize)
                .ToList()
        };
    }

    private static IReadOnlyList<CompletedProjectSummaryDto> Prioritise(
        IEnumerable<CompletedProjectSummaryDto> items,
        int queueSize) =>
        CompletedProjectCompletionOrdering
            .Apply(items, descending: true)
            .Take(queueSize)
            .ToList();

    private static bool AgeInRange(int? completedYear, int currentYear, int minimum, int maximum)
    {
        if (!completedYear.HasValue) return false;
        var age = currentYear - completedYear.Value;
        return age >= minimum && age <= maximum;
    }
}
