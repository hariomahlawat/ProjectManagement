using ProjectManagement.Models;

namespace ProjectManagement.Services.Notebook;

/// <summary>
/// Authoritative Notebook domain rules shared by service mutation paths.
/// The persisted content model is deliberately small: Note or Checklist.
/// Legacy item-type values are accepted only for backwards-compatible inputs
/// and are normalised before persistence.
/// </summary>
public static class NotebookRules
{
    private static readonly HashSet<string> AllowedColours = new(StringComparer.OrdinalIgnoreCase)
    {
        "white", "blue", "amber", "green", "rose", "slate"
    };

    public static NotebookItemType NormalizeContentType(NotebookItemType requestedType)
        => requestedType == NotebookItemType.Checklist
            ? NotebookItemType.Checklist
            : NotebookItemType.Note;

    public static bool IsSupportedCreateType(NotebookItemType requestedType)
        => requestedType is NotebookItemType.Note
            or NotebookItemType.Checklist
            or NotebookItemType.Reminder
            or NotebookItemType.Sticky
            or NotebookItemType.Idea
            or NotebookItemType.Draft;

    public static void ValidatePriority(NotebookPriority priority)
    {
        if (!Enum.IsDefined(priority))
        {
            throw new NotebookValidationException("Invalid notebook priority.");
        }
    }

    public static void ValidateReminder(DateTimeOffset? reminderAtUtc, DateTimeOffset nowUtc, bool required = false)
    {
        if (required && reminderAtUtc is null)
        {
            throw new NotebookValidationException("Choose a reminder date and time.");
        }

        if (reminderAtUtc.HasValue && reminderAtUtc.Value <= nowUtc)
        {
            throw new NotebookValidationException("Choose a future reminder date and time.");
        }
    }

    public static string CleanColour(string? colour)
    {
        if (string.IsNullOrWhiteSpace(colour))
        {
            return "white";
        }

        var normalised = colour.Trim().ToLowerInvariant();
        if (!AllowedColours.Contains(normalised))
        {
            throw new NotebookValidationException("Unsupported notebook colour.");
        }

        return normalised;
    }

    public static bool IsAllowedColour(string? colour)
        => string.IsNullOrWhiteSpace(colour) || AllowedColours.Contains(colour.Trim());
}
