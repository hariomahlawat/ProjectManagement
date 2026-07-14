namespace ProjectManagement.Services.Usage;

public enum ErpUsageActionKind
{
    Ignored = 0,
    Operational = 1,
    Administrative = 2
}

/// <summary>
/// Provides one authoritative classification for audited actions used by adoption analytics.
/// Authentication and telemetry plumbing are ignored; recognised mission/workflow activity is
/// operational; remaining meaningful audit activity is administrative.
/// </summary>
public static class ErpUsageActionClassifier
{
    private static readonly string[] IgnoredPrefixes =
    {
        "Login",
        "Logout",
        "Auth",
        "Password",
        "UserActivity",
        "ErpUsage",
        "SystemHealth",
        "Session",
        "Antiforgery"
    };

    private static readonly string[] OperationalPrefixes =
    {
        "Project",
        "Projects.",
        "Stage",
        "PlanVersion",
        "Approval",
        "Remark",
        "Comments.",
        "ActionTask",
        "ActionSprint",
        "Task",
        "Todo",
        "Calendar.",
        "CalendarEvent",
        "Celebration",
        "Document",
        "Documents.",
        "DocRepo",
        "Activity",
        "Visit",
        "Training",
        "Proliferation",
        "ProjectOfficeReports.",
        "Ipr",
        "Ffc",
        "IndustryPartner",
        "Notebook",
        "Notification"
    };

    private static readonly string[] AdministrativePrefixes =
    {
        "Admin",
        "Holiday",
        "MasterData",
        "Role",
        "User",
        "Media",
        "Maintenance",
        "Recovery",
        "Import",
        "Ocr",
        "Security"
    };

    public static ErpUsageActionKind Classify(string? action)
    {
        var value = action?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return ErpUsageActionKind.Ignored;
        }

        if (StartsWithAny(value, IgnoredPrefixes))
        {
            return ErpUsageActionKind.Ignored;
        }

        if (StartsWithAny(value, AdministrativePrefixes))
        {
            return ErpUsageActionKind.Administrative;
        }

        if (StartsWithAny(value, OperationalPrefixes)
            || ContainsOperationalVerb(value))
        {
            return ErpUsageActionKind.Operational;
        }

        return ErpUsageActionKind.Administrative;
    }

    private static bool StartsWithAny(string value, IEnumerable<string> prefixes) =>
        prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsOperationalVerb(string value) =>
        value.Contains("Approved", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Rejected", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Submitted", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Assigned", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Completed", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Uploaded", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Published", StringComparison.OrdinalIgnoreCase);
}
