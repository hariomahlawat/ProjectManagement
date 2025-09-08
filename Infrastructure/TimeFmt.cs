using System;

namespace ProjectManagement.Infrastructure
{
    public static class TimeFmt
    {
        public static string ToIst(DateTime? dt) =>
            dt is null ? "—" : IstClock.ToIst(dt.Value).ToString("dd MMM yyyy, HH:mm");
    }
}
