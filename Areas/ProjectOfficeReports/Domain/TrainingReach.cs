using System;
using System.Linq.Expressions;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

/// <summary>A training's headcount and the dates that place it in time.</summary>
public sealed record TrainingReachRow(DateOnly? StartDate, DateOnly? EndDate, int? TrainingYear, int? TrainingMonth, int Total);

/// <summary>
/// How many people a training reached and when it happened, resolved once for every view that
/// reports training reach (the Dashboard's Training reach, the Command desk's What moved forward).
/// </summary>
public static class TrainingReach
{
    /// <summary>
    /// People reached: the recorded counters, or for older records the legacy officer, JCO and
    /// OR counts. Translates to SQL; <see cref="Row"/> uses the same rule.
    /// </summary>
    public static readonly Expression<Func<Training, int>> HeadCount = training =>
        training.Counters != null
            ? training.Counters.Total
            : training.LegacyOfficerCount + training.LegacyJcoCount + training.LegacyOrCount;

    /// <summary>The headcount (as <see cref="HeadCount"/>) with the training's dates. Translates to SQL.</summary>
    public static readonly Expression<Func<Training, TrainingReachRow>> Row = training => new TrainingReachRow(
        training.StartDate,
        training.EndDate,
        training.TrainingYear,
        training.TrainingMonth,
        training.Counters != null
            ? training.Counters.Total
            : training.LegacyOfficerCount + training.LegacyJcoCount + training.LegacyOrCount);

    /// <summary>
    /// Trainings that may fall between two dates (inclusive): a start or end date in the range,
    /// or, with neither, a recorded month in it. <see cref="ReferenceDate"/> then places each one.
    /// Translates to SQL.
    /// </summary>
    public static Expression<Func<Training, bool>> MayFallBetween(DateOnly from, DateOnly to)
    {
        var fromMonthIndex = from.Year * 12 + from.Month;
        var toMonthIndex = to.Year * 12 + to.Month;
        return training =>
            (training.StartDate.HasValue && training.StartDate.Value >= from && training.StartDate.Value <= to)
            || (training.EndDate.HasValue && training.EndDate.Value >= from && training.EndDate.Value <= to)
            || (!training.StartDate.HasValue && !training.EndDate.HasValue
                && training.TrainingYear.HasValue && training.TrainingMonth.HasValue
                && training.TrainingYear.Value * 12 + training.TrainingMonth.Value >= fromMonthIndex
                && training.TrainingYear.Value * 12 + training.TrainingMonth.Value <= toMonthIndex);
    }

    /// <summary>The start date, else the end date, else the first of the recorded month.</summary>
    public static DateOnly? ReferenceDate(TrainingReachRow row)
        => row.StartDate
            ?? row.EndDate
            ?? (row.TrainingYear is int year && row.TrainingMonth is int month
                && month is >= 1 and <= 12 && year is > 0 and < 10000
                ? new DateOnly(year, month, 1)
                : null);
}
