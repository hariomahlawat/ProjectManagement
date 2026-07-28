using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;
using ProjectManagement.Models.Projects;

namespace ProjectManagement.Services.Projects;

// SECTION: Server-side portfolio focus codes used by the page, export and service.
public static class CompletedProjectPortfolioStatusCodes
{
    public const string FullyReady = "fully-ready";
    public const string AvailableBlocked = "available-blocked";
    public const string TechnologyAction = "technology-action";
    public const string TotAction = "tot-action";
    public const string CriticalIncomplete = "critical-incomplete";
    public const string TechnologyAssessmentPending = "technology-assessment-pending";

    public static readonly string[] All =
    {
        FullyReady,
        AvailableBlocked,
        TechnologyAction,
        TotAction,
        CriticalIncomplete,
        TechnologyAssessmentPending
    };

    public static string? Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalised = value.Trim().ToLowerInvariant();
        return All.Contains(normalised, StringComparer.Ordinal) ? normalised : null;
    }

    public static string GetLabel(string? value) => Normalise(value) switch
    {
        FullyReady => "Fully ready",
        AvailableBlocked => "Available but blocked",
        TechnologyAction => "Technology action required",
        TotAction => "ToT action pending",
        CriticalIncomplete => "Critical record incomplete",
        TechnologyAssessmentPending => "Technology assessment pending",
        _ => "All portfolio positions"
    };
}

// SECTION: Single source of truth for completed-project readiness and data quality.
public static class CompletedProjectPortfolioPolicy
{
    private static readonly string[] EmptyFields = Array.Empty<string>();

    public static bool IsFullyReady(CompletedProjectSummaryDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return string.Equals(item.TechStatus, ProjectTechStatusCodes.Current, StringComparison.OrdinalIgnoreCase)
               && item.AvailableForProliferation == true
               && item.TotStatus is ProjectTotStatus.Completed or ProjectTotStatus.NotRequired;
    }

    public static bool IsAvailableButBlocked(CompletedProjectSummaryDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.AvailableForProliferation == true && !IsFullyReady(item);
    }

    public static bool RequiresTechnologyAction(CompletedProjectSummaryDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return string.Equals(item.TechStatus, ProjectTechStatusCodes.Outdated, StringComparison.OrdinalIgnoreCase)
               || string.Equals(item.TechStatus, ProjectTechStatusCodes.Obsolete, StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasTotActionPending(CompletedProjectSummaryDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.TotStatus is ProjectTotStatus.NotStarted or ProjectTotStatus.InProgress;
    }

    public static bool IsTechnologyAssessmentPending(CompletedProjectSummaryDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return string.IsNullOrWhiteSpace(item.TechStatus);
    }

    public static IReadOnlyList<string> GetCriticalMissingFields(CompletedProjectSummaryDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        List<string>? fields = null;
        if (string.IsNullOrWhiteSpace(item.TechStatus)) Add(ref fields, "Technology assessment");
        if (!item.AvailableForProliferation.HasValue) Add(ref fields, "Proliferation decision");
        if (!item.TotStatus.HasValue) Add(ref fields, "ToT status");
        if (!item.RdCostLakhs.HasValue) Add(ref fields, "Development cost");

        if (fields is null)
        {
            return EmptyFields;
        }

        return fields;
    }

    public static IReadOnlyList<string> GetSupplementaryMissingFields(CompletedProjectSummaryDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        List<string>? fields = null;
        if (!item.ApproxProductionCost.HasValue) Add(ref fields, "Production cost");
        if (item.LatestLpp is null) Add(ref fields, "Latest LPP");

        if (fields is null)
        {
            return EmptyFields;
        }

        return fields;
    }

    public static int GetCriticalMissingCount(CompletedProjectSummaryDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var count = 0;
        if (string.IsNullOrWhiteSpace(item.TechStatus)) count++;
        if (!item.AvailableForProliferation.HasValue) count++;
        if (!item.TotStatus.HasValue) count++;
        if (!item.RdCostLakhs.HasValue) count++;
        return count;
    }

    public static int GetSupplementaryMissingCount(CompletedProjectSummaryDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var count = 0;
        if (!item.ApproxProductionCost.HasValue) count++;
        if (item.LatestLpp is null) count++;
        return count;
    }

    public static int GetTotalMissingCount(CompletedProjectSummaryDto item) =>
        GetCriticalMissingCount(item) + GetSupplementaryMissingCount(item);

    public static string GetReadinessBlockers(CompletedProjectSummaryDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var blockers = new List<string>();

        if (string.IsNullOrWhiteSpace(item.TechStatus))
        {
            blockers.Add("Technology not assessed");
        }
        else if (string.Equals(item.TechStatus, ProjectTechStatusCodes.Outdated, StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("Technology refresh required");
        }
        else if (string.Equals(item.TechStatus, ProjectTechStatusCodes.Obsolete, StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("Technology obsolete");
        }
        else if (!string.Equals(item.TechStatus, ProjectTechStatusCodes.Current, StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add($"Technology: {item.TechStatus}");
        }

        if (item.AvailableForProliferation is null)
        {
            blockers.Add("Proliferation decision not recorded");
        }
        else if (item.AvailableForProliferation == false)
        {
            blockers.Add("Not available for proliferation");
        }

        switch (item.TotStatus)
        {
            case null:
                blockers.Add("ToT status not recorded");
                break;
            case ProjectTotStatus.NotStarted:
                blockers.Add("ToT not started");
                break;
            case ProjectTotStatus.InProgress:
                blockers.Add("ToT in progress");
                break;
        }

        return blockers.Count == 0 ? "No readiness blockers" : string.Join(" · ", blockers);
    }

    public static bool MatchesPortfolioStatus(CompletedProjectSummaryDto item, string? portfolioStatus)
    {
        ArgumentNullException.ThrowIfNull(item);

        return CompletedProjectPortfolioStatusCodes.Normalise(portfolioStatus) switch
        {
            CompletedProjectPortfolioStatusCodes.FullyReady => IsFullyReady(item),
            CompletedProjectPortfolioStatusCodes.AvailableBlocked => IsAvailableButBlocked(item),
            CompletedProjectPortfolioStatusCodes.TechnologyAction => RequiresTechnologyAction(item),
            CompletedProjectPortfolioStatusCodes.TotAction => HasTotActionPending(item),
            CompletedProjectPortfolioStatusCodes.CriticalIncomplete => GetCriticalMissingCount(item) > 0,
            CompletedProjectPortfolioStatusCodes.TechnologyAssessmentPending => IsTechnologyAssessmentPending(item),
            _ => true
        };
    }

    public static string GetTechnologyLabel(string? status) =>
        string.IsNullOrWhiteSpace(status) ? "Not assessed" : status;

    public static string GetAvailabilityLabel(bool? available) => available switch
    {
        true => "Available",
        false => "Not available",
        _ => "Not assessed"
    };

    public static string GetTotLabel(ProjectTotStatus? status) => status switch
    {
        ProjectTotStatus.Completed => "Completed",
        ProjectTotStatus.InProgress => "In progress",
        ProjectTotStatus.NotStarted => "Not started",
        ProjectTotStatus.NotRequired => "Not required",
        _ => "Not assessed"
    };

    private static void Add(ref List<string>? fields, string value)
    {
        fields ??= new List<string>();
        fields.Add(value);
    }
}
