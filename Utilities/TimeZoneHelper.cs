using TimeZoneConverter;

namespace ProjectManagement.Utilities
{
    public static class TimeZoneHelper
    {
        // Always returns a valid IST zone on Windows or Linux
        public static TimeZoneInfo GetIst()
        {
            // TZConvert maps IANA <-> Windows reliably
            return TZConvert.GetTimeZoneInfo("Asia/Kolkata");
        }
    }
}
