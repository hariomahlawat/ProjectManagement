using ProjectManagement.Infrastructure;
using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Tests.Fakes;

public sealed class FixedActionTrackerClock : IActionTrackerClock
{
    public static DateTime DefaultUtcNow { get; } = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    public DateTime UtcNow => DefaultUtcNow;
    public DateTime UtcToday => UtcNow.Date;
    public DateTime IstNow => IstClock.ToIst(UtcNow);
    public DateTime IstToday => IstNow.Date;
}
