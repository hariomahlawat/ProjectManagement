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
    }
}
