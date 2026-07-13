using System.Text.RegularExpressions;

namespace ProjectManagement.Services.Admin;

public sealed record AuditActionPresentation(
    string Label,
    string Icon,
    string Tone = "neutral");

public interface IAuditActionPresentationCatalog
{
    AuditActionPresentation Describe(string? action, string? level = null);
}

public sealed partial class AuditActionPresentationCatalog : IAuditActionPresentationCatalog
{
    private static readonly IReadOnlyDictionary<string, AuditActionPresentation> KnownActions =
        new Dictionary<string, AuditActionPresentation>(StringComparer.OrdinalIgnoreCase)
        {
            ["AdminUserCreated"] = new("User account created", "bi-person-plus", "success"),
            ["AdminUserUpdated"] = new("User profile and roles updated", "bi-person-gear", "neutral"),
            ["AdminUserPasswordReset"] = new("User password reset", "bi-key", "warning"),
            ["AdminUserDisabled"] = new("User account disabled", "bi-person-slash", "warning"),
            ["AdminUserEnabled"] = new("User account enabled", "bi-person-check", "success"),
            ["AdminUserDeleteRequested"] = new("User deletion requested", "bi-person-x", "danger"),
            ["AdminUserDeleteUndone"] = new("User deletion request withdrawn", "bi-arrow-counterclockwise", "success"),
            ["AdminUserPurged"] = new("User account permanently removed", "bi-person-dash", "danger"),
            ["AdminUserDeleted"] = new("User account deleted", "bi-person-dash", "danger"),
            ["Projects.ActualsUpdated"] = new("Project actuals updated", "bi-cash-stack", "neutral"),
            ["Projects.MetaChangedDirect"] = new("Project details changed", "bi-pencil-square", "neutral"),
            ["Projects.AssignRoles"] = new("Project roles assigned", "bi-people", "neutral"),
            ["Projects.StageChanged"] = new("Project stage changed", "bi-signpost-split", "neutral"),
            ["Project.Trashed"] = new("Project moved to trash", "bi-trash3", "warning"),
            ["Project.Restored"] = new("Project restored", "bi-arrow-counterclockwise", "success"),
            ["Documents.Restored"] = new("Document restored", "bi-file-earmark-check", "success"),
            ["Documents.Purged"] = new("Document permanently deleted", "bi-file-earmark-x", "danger"),
            ["Calendar.EventRestored"] = new("Calendar event restored", "bi-calendar-check", "success"),
            ["MasterData.Created"] = new("Master-data item created", "bi-plus-circle", "success"),
            ["MasterData.Updated"] = new("Master-data item updated", "bi-pencil-square", "neutral"),
            ["MasterData.Deactivated"] = new("Master-data item deactivated", "bi-slash-circle", "warning")
        };

    public AuditActionPresentation Describe(string? action, string? level = null)
    {
        var normalizedAction = action?.Trim();
        var toneFromLevel = ResolveTone(level);

        if (!string.IsNullOrWhiteSpace(normalizedAction)
            && KnownActions.TryGetValue(normalizedAction, out var known))
        {
            return string.Equals(toneFromLevel, "neutral", StringComparison.Ordinal)
                ? known
                : known with { Tone = toneFromLevel };
        }

        var label = Humanise(normalizedAction);
        var tone = string.Equals(toneFromLevel, "neutral", StringComparison.Ordinal)
            ? InferTone(normalizedAction)
            : toneFromLevel;

        return new AuditActionPresentation(label, InferIcon(normalizedAction), tone);
    }

    private static string Humanise(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return "Administrative action";
        }

        var text = action
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ');
        text = PascalBoundaryRegex().Replace(text, "$1 $2");
        text = MultiSpaceRegex().Replace(text, " ").Trim();

        return text.Length == 0
            ? "Administrative action"
            : char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string ResolveTone(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        "error" or "critical" => "danger",
        "warning" => "warning",
        "success" => "success",
        _ => "neutral"
    };

    private static string InferTone(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return "neutral";
        }

        if (action.Contains("Delete", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Purge", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Failed", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Error", StringComparison.OrdinalIgnoreCase))
        {
            return "danger";
        }

        if (action.Contains("Disable", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Trash", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Warning", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Reset", StringComparison.OrdinalIgnoreCase))
        {
            return "warning";
        }

        if (action.Contains("Create", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Enable", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Restore", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Success", StringComparison.OrdinalIgnoreCase))
        {
            return "success";
        }

        return "neutral";
    }

    private static string InferIcon(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return "bi-activity";
        }

        if (action.Contains("User", StringComparison.OrdinalIgnoreCase)) return "bi-person-gear";
        if (action.Contains("Project", StringComparison.OrdinalIgnoreCase)) return "bi-kanban";
        if (action.Contains("Document", StringComparison.OrdinalIgnoreCase)) return "bi-file-earmark-text";
        if (action.Contains("Calendar", StringComparison.OrdinalIgnoreCase)) return "bi-calendar-event";
        if (action.Contains("Login", StringComparison.OrdinalIgnoreCase)) return "bi-box-arrow-in-right";
        if (action.Contains("Database", StringComparison.OrdinalIgnoreCase)) return "bi-database";

        return "bi-activity";
    }

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex PascalBoundaryRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex MultiSpaceRegex();
}
