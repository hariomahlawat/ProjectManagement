using System.Text.RegularExpressions;

namespace ProjectManagement.Services.Admin;

public sealed record AuditActionPresentation(
    string Label,
    string Icon,
    string Tone = "neutral",
    string Category = "Other",
    string? EntityType = null);

public interface IAuditActionPresentationCatalog
{
    AuditActionPresentation Describe(string? action, string? level = null);
}

public sealed partial class AuditActionPresentationCatalog : IAuditActionPresentationCatalog
{
    private static readonly IReadOnlyDictionary<string, AuditActionPresentation> KnownActions =
        new Dictionary<string, AuditActionPresentation>(StringComparer.OrdinalIgnoreCase)
        {
            ["AdminUserCreated"] = new("User account created", "bi-person-plus", "success", "Access & security", "ApplicationUser"),
            ["AdminUserUpdated"] = new("User profile and roles updated", "bi-person-gear", "neutral", "Access & security", "ApplicationUser"),
            ["AdminUserPasswordReset"] = new("User password reset", "bi-key", "warning", "Access & security", "ApplicationUser"),
            ["AdminUserDisabled"] = new("User account disabled", "bi-person-slash", "warning", "Access & security", "ApplicationUser"),
            ["AdminUserEnabled"] = new("User account enabled", "bi-person-check", "success", "Access & security", "ApplicationUser"),
            ["AdminUserDeleteRequested"] = new("User deletion requested", "bi-person-x", "danger", "Access & security", "ApplicationUser"),
            ["AdminUserDeleteUndone"] = new("User deletion request withdrawn", "bi-arrow-counterclockwise", "success", "Access & security", "ApplicationUser"),
            ["AdminUserPurged"] = new("User account permanently removed", "bi-person-dash", "danger", "Access & security", "ApplicationUser"),
            ["AdminUserDeleted"] = new("User account deleted", "bi-person-dash", "danger", "Access & security", "ApplicationUser"),
            [AuthenticationEventNames.AuditLoginSuccess] = new("Successful sign-in", "bi-box-arrow-in-right", "success", "Authentication", "ApplicationUser"),
            [AuthenticationEventNames.AuditLoginFailed] = new("Failed sign-in", "bi-shield-exclamation", "warning", "Authentication", "ApplicationUser"),
            [AuthenticationEventNames.AuditLoginLockedOut] = new("Sign-in blocked — account locked", "bi-lock", "danger", "Authentication", "ApplicationUser"),
            ["Projects.ActualsUpdated"] = new("Project actuals updated", "bi-cash-stack", "neutral", "Projects", "Project"),
            ["Projects.MetaChangedDirect"] = new("Project details changed", "bi-pencil-square", "neutral", "Projects", "Project"),
            ["Projects.AssignRoles"] = new("Project roles assigned", "bi-people", "neutral", "Projects", "Project"),
            ["Projects.StageChanged"] = new("Project stage changed", "bi-signpost-split", "neutral", "Projects", "Project"),
            ["Archive"] = new("Project archived", "bi-archive", "neutral", "Recovery", "Project"),
            ["RestoreArchive"] = new("Archived project restored", "bi-folder2-open", "success", "Recovery", "Project"),
            ["Trash"] = new("Project moved to trash", "bi-trash3", "warning", "Recovery", "Project"),
            ["RestoreTrash"] = new("Project restored from trash", "bi-arrow-counterclockwise", "success", "Recovery", "Project"),
            ["Projects.Purge"] = new("Project permanently deleted", "bi-trash3-fill", "danger", "Recovery", "Project"),
            ["Project.Trashed"] = new("Project moved to trash", "bi-trash3", "warning", "Recovery", "Project"),
            ["Project.Restored"] = new("Project restored", "bi-arrow-counterclockwise", "success", "Recovery", "Project"),
            ["ProjectPurgeAuthorised"] = new("Project purge authorised", "bi-shield-exclamation", "warning", "Recovery", "Project"),
            ["Project.Purged"] = new("Project permanently deleted", "bi-trash3-fill", "danger", "Recovery", "Project"),
            ["Documents.Restored"] = new("Document restored", "bi-file-earmark-check", "success", "Recovery", "Document"),
            ["Documents.Purged"] = new("Document permanently deleted", "bi-file-earmark-x", "danger", "Recovery", "Document"),
            ["Calendar.EventRestored"] = new("Calendar event restored", "bi-calendar-check", "success", "Recovery", "Event"),
            ["CalendarEventRestored"] = new("Calendar event restored", "bi-calendar-check", "success", "Recovery", "Event"),
            ["PdfIngestionStarted"] = new("PDF ingestion started", "bi-file-earmark-arrow-up", "neutral", "Maintenance", "DocumentRepository"),
            ["PdfIngestionCompleted"] = new("PDF ingestion completed", "bi-file-earmark-check", "success", "Maintenance", "DocumentRepository"),
            ["PdfIngestionCancelled"] = new("PDF ingestion cancelled", "bi-slash-circle", "warning", "Maintenance", "DocumentRepository"),
            ["PdfIngestionFailed"] = new("PDF ingestion failed", "bi-file-earmark-x", "danger", "Maintenance", "DocumentRepository"),
            ["LegacyImportCommitted"] = new("Legacy project import committed", "bi-database-check", "success", "Maintenance", "ProjectLegacyImport"),
            ["MasterData.Created"] = new("Master-data item created", "bi-plus-circle", "success", "Master data"),
            ["MasterData.Updated"] = new("Master-data item updated", "bi-pencil-square", "neutral", "Master data"),
            ["MasterData.Deactivated"] = new("Master-data item deactivated", "bi-slash-circle", "warning", "Master data")
        };

    public AuditActionPresentation Describe(string? action, string? level = null)
    {
        var normalizedAction = action?.Trim();
        var toneFromLevel = ResolveTone(level);

        if (!string.IsNullOrWhiteSpace(normalizedAction)
            && KnownActions.TryGetValue(normalizedAction, out var known))
        {
            // The catalogue is authoritative for known business actions. A broad
            // stored log level must not turn routine metadata operations into warnings.
            // A critical/error level may still escalate a normally neutral action.
            return string.Equals(toneFromLevel, "danger", StringComparison.Ordinal)
                && string.Equals(known.Tone, "neutral", StringComparison.Ordinal)
                    ? known with { Tone = "danger" }
                    : known;
        }

        var label = Humanise(normalizedAction);
        var tone = string.Equals(toneFromLevel, "neutral", StringComparison.Ordinal)
            ? InferTone(normalizedAction)
            : toneFromLevel;

        return new AuditActionPresentation(
            label,
            InferIcon(normalizedAction),
            tone,
            InferCategory(normalizedAction),
            InferEntityType(normalizedAction));
    }

    private static string Humanise(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return "Administrative action";

        var text = action.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
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
        if (string.IsNullOrWhiteSpace(action)) return "neutral";
        if (ContainsAny(action, "Delete", "Purge", "Failed", "Error", "LockedOut")) return "danger";
        if (ContainsAny(action, "Disable", "Trash", "Warning", "Reset")) return "warning";
        if (ContainsAny(action, "Create", "Enable", "Restore", "Success")) return "success";
        return "neutral";
    }

    private static string InferIcon(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return "bi-activity";
        if (action.Contains("User", StringComparison.OrdinalIgnoreCase)) return "bi-person-gear";
        if (action.Contains("Project", StringComparison.OrdinalIgnoreCase)) return "bi-kanban";
        if (action.Contains("Document", StringComparison.OrdinalIgnoreCase)) return "bi-file-earmark-text";
        if (action.Contains("Calendar", StringComparison.OrdinalIgnoreCase)) return "bi-calendar-event";
        if (action.Contains("Login", StringComparison.OrdinalIgnoreCase)) return "bi-box-arrow-in-right";
        if (action.Contains("Database", StringComparison.OrdinalIgnoreCase)) return "bi-database";
        if (action.Contains("Ingestion", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Import", StringComparison.OrdinalIgnoreCase)) return "bi-tools";
        return "bi-activity";
    }

    private static string InferCategory(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return "Other";
        if (action.Contains("Login", StringComparison.OrdinalIgnoreCase)) return "Authentication";
        if (action.Contains("User", StringComparison.OrdinalIgnoreCase)) return "Access & security";
        if (action.Contains("Project", StringComparison.OrdinalIgnoreCase)) return "Projects";
        if (action.Contains("Document", StringComparison.OrdinalIgnoreCase)) return "Documents";
        if (action.Contains("Calendar", StringComparison.OrdinalIgnoreCase)) return "Calendar";
        if (action.Contains("Master", StringComparison.OrdinalIgnoreCase)) return "Master data";
        if (action.Contains("Ingestion", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Import", StringComparison.OrdinalIgnoreCase)) return "Maintenance";
        if (ContainsAny(action, "Restore", "Trash", "Purge", "Recycle")) return "Recovery";
        return "Other";
    }

    private static string? InferEntityType(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return null;
        if (action.Contains("User", StringComparison.OrdinalIgnoreCase)) return "ApplicationUser";
        if (action.Contains("Project", StringComparison.OrdinalIgnoreCase)) return "Project";
        if (action.Contains("Document", StringComparison.OrdinalIgnoreCase)) return "Document";
        if (action.Contains("Calendar", StringComparison.OrdinalIgnoreCase)) return "Event";
        return null;
    }

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex PascalBoundaryRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex MultiSpaceRegex();
}
