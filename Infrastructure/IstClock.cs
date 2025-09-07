using System;
using System.Runtime.InteropServices;

namespace ProjectManagement.Infrastructure
{
    public static class IstClock
    {
        private static readonly TimeZoneInfo _ist = TimeZoneInfo.FindSystemTimeZoneById(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "India Standard Time" : "Asia/Kolkata");

        public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _ist);

        public static DateTimeOffset NowOffset => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _ist);
    }
}
