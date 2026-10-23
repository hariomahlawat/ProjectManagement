using System;
using System.Linq.Expressions;

namespace ProjectManagement.Models.Activities
{
    /// <summary>
    /// When an institutional activity counts as having happened: its scheduled start, else when
    /// it was recorded. Deleted activities and ones still in the future never count. Shared by the
    /// Dashboard and the Command desk so they agree.
    /// </summary>
    public static class ActivityOccurrence
    {
        /// <summary>Activities that happened at or before <paramref name="untilUtc"/> (and, if given, at or after <paramref name="fromUtc"/>).</summary>
        public static Expression<Func<Activity, bool>> Between(DateTimeOffset? fromUtc, DateTimeOffset untilUtc)
            => fromUtc is DateTimeOffset from
                ? activity => !activity.IsDeleted
                    && (activity.ScheduledStartUtc ?? activity.CreatedAtUtc) >= from
                    && (activity.ScheduledStartUtc ?? activity.CreatedAtUtc) <= untilUtc
                : activity => !activity.IsDeleted
                    && (activity.ScheduledStartUtc ?? activity.CreatedAtUtc) <= untilUtc;
    }
}
