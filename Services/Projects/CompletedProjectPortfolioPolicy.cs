using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;
using ProjectManagement.Models.Projects;

namespace ProjectManagement.Services.Projects;

// SECTION: Server-side portfolio focus codes used by the page, export and service.
public static class CompletedProjectPortfolioStatusCodes
{
    public const string ProliferationAssessmentPending = "proliferation-assessment-pending";
    public const string TechnologyAction = "technology-action";
    public const string TotAction = "tot-action";
    public const string CriticalIncomplete = "critical-incomplete";
    public const string TechnologyAssessmentPending = "technology-assessment-pending";

    public static readonly string[] All =
    {
        ProliferationAssessmentPending,
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
        ProliferationAssessmentPending => "Proliferation assessment pending",
        TechnologyAction => "Technology review required",
        TotAction => "ToT action pending",
        CriticalIncomplete => "Records with critical gaps",
        TechnologyAssessmentPending => "Technology assessment pending",
        _ => "All portfolio positions"
    };
}

// SECTION: Single source of truth for completed-project action and data-quality rules.
public static class CompletedProjectPortfolioPolicy
{
    private static readonly string[] EmptyFields = Array.Empty<string>();

    public static bool IsProliferationAssessmentPending(CompletedProjectSummaryDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return !item.AvailableForProliferation.HasValue;
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
        return !item.IsBuild && (item.TotStatus is ProjectTotStatus.NotStarted or ProjectTotStatus.InProgress);
    }

    public static bool IsTechnologyAssessmentPending(CompletedProjectSummaryDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return string.IsNullOrWhiteSpace(item.TechStatus);
    }

    public static IReadOnlyList<string> GetActionItems(CompletedProjectSummaryDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        List<string>? actions = null;

        if (string.IsNullOrWhiteSpace(item.TechStatus))
        {
            Add(ref actions, "Record the technology assessment");
        }
        else if (string.Equals(item.TechStatus, ProjectTechStatusCodes.Outdated, StringComparison.OrdinalIgnoreCase))
        {
            Add(ref actions, "Review technology refresh requirements");
        }
        else if (string.Equals(item.TechStatus, ProjectTechStatusCodes.Obsolete, StringComparison.OrdinalIgnoreCase))
        {
            Add(ref actions, "Review retention or replacement of obsolete technology");
        }

        if (!item.AvailableForProliferation.HasValue)
        {
            Add(ref actions, "Record the proliferation decision");
        }

        if (!item.IsBuild)
        {
            switch (item.TotStatus)
            {
                case null:
                    Add(ref actions, "Record the ToT status");
                    break;
                case ProjectTotStatus.NotStarted:
                    Add(ref actions, "Initiate the required ToT action");
                    break;
                case ProjectTotStatus.InProgress:
                    Add(ref actions, "Complete the pending ToT action");
                    break;
            }
        }

        if (actions is null)
        {
            return EmptyFields;
        }

        return actions;
    }

    public static IReadOnlyList<string> GetCriticalMissingFields(CompletedProjectSummaryDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        List<string>? fields = null;
        if (string.IsNullOrWhiteSpace(item.TechStatus)) Add(ref fields, "Technology assessment");
        if (!item.AvailableForProliferation.HasValue) Add(ref fields, "Proliferation decision");
        if (!item.IsBuild && !item.TotStatus.HasValue) Add(ref fields, "ToT status");
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
        if (!item.ProliferationCostLakhs.HasValue) Add(ref fields, "Proliferation cost");
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
        if (!item.IsBuild && !item.TotStatus.HasValue) count++;
        if (!item.RdCostLakhs.HasValue) count++;
        return count;
    }

    public static int GetSupplementaryMissingCount(CompletedProjectSummaryDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var count = 0;
        if (!item.ProliferationCostLakhs.HasValue) count++;
        if (item.LatestLpp is null) count++;
        return count;
    }

    public static int GetTotalMissingCount(CompletedProjectSummaryDto item) =>
        GetCriticalMissingCount(item) + GetSupplementaryMissingCount(item);

    public static bool MatchesPortfolioStatus(CompletedProjectSummaryDto item, string? portfolioStatus)
    {
        ArgumentNullException.ThrowIfNull(item);

        return CompletedProjectPortfolioStatusCodes.Normalise(portfolioStatus) switch
        {
            CompletedProjectPortfolioStatusCodes.ProliferationAssessmentPending => IsProliferationAssessmentPending(item),
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

    public static string GetTotLabel(ProjectTotStatus? status, bool isBuild = false)
    {
        if (isBuild) return "Not applicable";

        return status switch
        {
            ProjectTotStatus.Completed => "Completed",
            ProjectTotStatus.InProgress => "In progress",
            ProjectTotStatus.NotStarted => "Not started",
            ProjectTotStatus.NotRequired => "Not required",
            _ => "Not assessed"
        };
    }

    private static void Add(ref List<string>? items, string value)
    {
        items ??= new List<string>();
        items.Add(value);
    }
}
