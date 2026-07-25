using System.Globalization;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Projects;

public static class ProjectCompletionFormatter
{
    public static ProjectCompletionPrecision InferPrecision(
        DateOnly? completedOn,
        int? completedYear,
        short? completedMonth)
    {
        if (completedOn.HasValue)
        {
            return ProjectCompletionPrecision.ExactDate;
        }

        if (completedYear.HasValue && completedMonth.HasValue)
        {
            return ProjectCompletionPrecision.MonthAndYear;
        }

        return completedYear.HasValue
            ? ProjectCompletionPrecision.YearOnly
            : ProjectCompletionPrecision.NotKnown;
    }

    public static string Format(
        DateOnly? completedOn,
        int? completedYear,
        short? completedMonth,
        string unknownText = "Not recorded")
    {
        if (completedOn.HasValue)
        {
            return completedOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        }

        if (completedYear.HasValue && completedMonth is >= 1 and <= 12)
        {
            return new DateOnly(completedYear.Value, completedMonth.Value, 1)
                .ToString("MMM yyyy", CultureInfo.InvariantCulture);
        }

        return completedYear?.ToString(CultureInfo.InvariantCulture) ?? unknownText;
    }

    public static string ToMonthInputValue(int? completedYear, short? completedMonth) =>
        completedYear.HasValue && completedMonth is >= 1 and <= 12
            ? FormattableString.Invariant($"{completedYear.Value:0000}-{completedMonth.Value:00}")
            : string.Empty;
}
