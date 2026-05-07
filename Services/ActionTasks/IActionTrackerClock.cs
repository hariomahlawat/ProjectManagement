using System;

namespace ProjectManagement.Services.ActionTasks;

public interface IActionTrackerClock
{
    // SECTION: UtcNow provides timestamp access for Action Tracker date and lockout decisions.
    DateTime UtcNow { get; }

    // SECTION: UtcToday keeps date-only Action Tracker calculations consistent across builders and pages.
    DateTime UtcToday { get; }
}

public sealed class SystemActionTrackerClock : IActionTrackerClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime UtcToday => DateTime.UtcNow.Date;
}
