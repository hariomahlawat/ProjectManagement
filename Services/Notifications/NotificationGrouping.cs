using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Notifications;

namespace ProjectManagement.Services.Notifications;

/// <summary>
/// Decides which notifications the bell may collapse into one group row.
/// </summary>
/// <remarks>
/// Routine updates for the same project, category and India-time day share a group key, so a
/// burst of document uploads or stage changes shows as one expandable row. Anything that needs
/// the recipient's action, or carries more than normal priority, has no key and always stays
/// on its own row. Clients only collapse rows whose keys are equal; this class owns the rule.
///
/// Senior roles (Comdt, MCO and HoD) receive remarks across many projects, so for them all of a
/// day's routine remark notifications share one key regardless of project. Everyone else keeps
/// one group per project. Mentions are a separate, action-required kind and are never grouped.
/// </remarks>
public static class NotificationGrouping
{
    public const string NormalPriority = "Normal";

    /// <summary>Roles whose remark notifications are combined across projects.</summary>
    public static readonly string[] CrossProjectRemarkRoles = { "Comdt", "MCO", "HoD" };

    public static bool CombinesRemarksAcrossProjects(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return CrossProjectRemarkRoles.Any(principal.IsInRole);
    }

    public static string? ResolveGroupKey(
        NotificationPresentation presentation,
        int? projectId,
        DateTime createdUtc)
        => ResolveGroupKey(kind: null, presentation, projectId, createdUtc, combineRemarksAcrossProjects: false);

    public static string? ResolveGroupKey(
        NotificationKind? kind,
        NotificationPresentation presentation,
        int? projectId,
        DateTime createdUtc,
        bool combineRemarksAcrossProjects)
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

        if (combineRemarksAcrossProjects && kind == NotificationKind.RemarkCreated)
        {
            return string.Create(CultureInfo.InvariantCulture, $"all-projects|remarks|{istDay}");
        }

        var project = projectId?.ToString(CultureInfo.InvariantCulture) ?? "none";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{project}|{presentation.Category}|{istDay}");
    }
}
