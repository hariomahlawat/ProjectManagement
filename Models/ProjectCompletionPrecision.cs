namespace ProjectManagement.Models;

/// <summary>
/// Describes the level of precision available for a project's completion information.
/// </summary>
public enum ProjectCompletionPrecision
{
    NotKnown = 0,
    YearOnly = 1,
    MonthAndYear = 2,
    ExactDate = 3
}
