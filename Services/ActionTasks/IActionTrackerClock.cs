using System;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Services.ActionTasks;

public interface IActionTrackerClock
{
    // SECTION: UtcNow provides timestamp access for Action Tracker date and lockout decisions.
    DateTime UtcNow { get; }

    // SECTION: UtcToday preserves legacy UTC calendar access for persisted-time diagnostics.
    DateTime UtcToday { get; }

    // SECTION: IstNow converts the canonical UTC instant into the Action Tracker business timezone.
    DateTime IstNow { get; }

    // SECTION: IstToday drives date-only business rules on the Indian calendar date.
    DateTime IstToday { get; }

    // SECTION: ConvertUtcToIst centralizes display conversion for Action Tracker timestamps.
    DateTime ConvertUtcToIst(DateTime utcDateTime) => IstClock.ToIst(utcDateTime);
}

public sealed class SystemActionTrackerClock : IActionTrackerClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime UtcToday => UtcNow.Date;

    public DateTime IstNow => IstClock.ToIst(UtcNow);

    public DateTime IstToday => IstNow.Date;

    public DateTime ConvertUtcToIst(DateTime utcDateTime) => IstClock.ToIst(utcDateTime);
}
