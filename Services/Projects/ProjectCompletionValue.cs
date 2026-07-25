using ProjectManagement.Models;

namespace ProjectManagement.Services.Projects;

/// <summary>
/// A completion value supplied by the user. Only the fields required by
/// <see cref="Precision"/> are persisted.
/// </summary>
public sealed record ProjectCompletionValue(
    ProjectCompletionPrecision Precision,
    DateOnly? ExactDate = null,
    int? Year = null,
    int? Month = null)
{
    public static ProjectCompletionValue NotKnown() =>
        new(ProjectCompletionPrecision.NotKnown);

    public static ProjectCompletionValue YearOnly(int year) =>
        new(ProjectCompletionPrecision.YearOnly, Year: year);

    public static ProjectCompletionValue MonthAndYear(int year, int month) =>
        new(ProjectCompletionPrecision.MonthAndYear, Year: year, Month: month);

    public static ProjectCompletionValue Exact(DateOnly date) =>
        new(ProjectCompletionPrecision.ExactDate, ExactDate: date);
}
