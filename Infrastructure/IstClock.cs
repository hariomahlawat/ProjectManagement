using System;
using ProjectManagement.Utilities;

namespace ProjectManagement.Infrastructure
{
    public static class IstClock
    {
        private static readonly TimeZoneInfo _ist = TimeZoneHelper.GetIst();

        public static TimeZoneInfo TimeZone => _ist;

        public static DateTime ToIst(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _ist);

        public static DateTime? ToIst(DateTime? utc) =>
            utc.HasValue ? ToIst(utc.Value) : (DateTime?)null;

        // SECTION: Application day-boundary conversion helpers
        public static DateTimeOffset StartOfDayIstToUtc(DateOnly date)
        {
            var istStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(istStart, _ist), TimeSpan.Zero);
        }

        public static DateTimeOffset ExclusiveEndOfDayIstToUtc(DateOnly date) =>
            StartOfDayIstToUtc(date.AddDays(1));

        public static DateTimeOffset ToIst(DateTimeOffset utc) =>
            TimeZoneInfo.ConvertTime(utc, _ist);

        public static DateTimeOffset? ToIst(DateTimeOffset? utc) =>
            utc.HasValue ? ToIst(utc.Value) : (DateTimeOffset?)null;
    }
}
