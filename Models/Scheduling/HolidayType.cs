namespace ProjectManagement.Models.Scheduling;

/// <summary>
/// Identifies the source classification of a holiday notification. Classification is
/// intentionally separate from the office's decision to observe a Restricted Holiday.
/// </summary>
public enum HolidayType
{
    Gazetted = 1,
    Restricted = 2
}
