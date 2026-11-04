using System;
using System.Linq;
using ProjectManagement.Models.Notifications;

namespace ProjectManagement.Services.Notifications;

/// <summary>
/// Keeps notification reads safe while two application versions run side by side.
/// </summary>
/// <remarks>
/// <see cref="NotificationKind"/> is stored as its name. When a newer release adds a kind and
/// writes rows with it, an older instance still running during a rolling deployment cannot
/// convert that name back to an enum value, so materialising the row throws. One such row would
/// otherwise fail the whole dispatcher batch (and keep it locked), the bell, the Notification
/// Centre and the retention sweep on the older instance.
///
/// Every query that materialises notification or dispatch entities is therefore limited to the
/// kinds this build knows. Rows with a newer kind are left untouched for an instance that
/// understands them. For the current build this filter matches every row.
/// </remarks>
internal static class NotificationKindCompatibility
{
    public static NotificationKind[] KnownKinds { get; } = Enum.GetValues<NotificationKind>();

    /// <summary>Rows with no kind (legacy) or a kind this build can read.</summary>
    public static IQueryable<Notification> WhereKindIsReadable(this IQueryable<Notification> query)
    {
        var known = KnownKinds;
        return query.Where(n => !n.Kind.HasValue || known.Contains(n.Kind.Value));
    }

    /// <summary>Dispatches whose kind this build can read.</summary>
    public static IQueryable<NotificationDispatch> WhereKindIsReadable(this IQueryable<NotificationDispatch> query)
    {
        var known = KnownKinds;
        return query.Where(d => known.Contains(d.Kind));
    }
}
