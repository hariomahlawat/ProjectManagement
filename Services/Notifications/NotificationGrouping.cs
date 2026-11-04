using System;
using System.Globalization;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Services.Notifications;

/// <summary>
/// Decides which notifications the bell may collapse into one group row.
/// </summary>
/// <remarks>
/// Routine updates for the same project, category and India-time day share a group key, so a
/// burst of document uploads or stage changes shows as one expandable row. Anything that needs
/// the recipient's action, or carries more than normal priority, has no key and always stays
/// on its own row. Clients only collapse rows whose keys are equal; this class owns the rule.
/// </remarks>
public static class NotificationGrouping
{
    public const string NormalPriority = "Normal";

    public static string? ResolveGroupKey(
        NotificationPresentation presentation,
        int? projectId,
        DateTime createdUtc)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (presentation.IsActionRequired
            || !string.Equals(presentation.Priority, NormalPriority, StringComparison.Ordinal))
        {
            return null;
        }

        var utc = createdUtc.Kind == DateTimeKind.Utc
            ? createdUtc
            : DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
        var istDay = IstClock.ToIst(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var project = projectId?.ToString(CultureInfo.InvariantCulture) ?? "none";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{project}|{presentation.Category}|{istDay}");
    }
}
