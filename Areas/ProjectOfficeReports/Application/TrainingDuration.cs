using System;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

// -----------------------------------------------------------------------------
// The length of a training in days, counted inclusively (a one-day training is 1).
// One rule for every output: the register, the export workbook and any other view
// format this value rather than working it out again.
// -----------------------------------------------------------------------------
public static class TrainingDuration
{
    // Needs a start date; a missing end means a single day. An end before the start
    // gives no duration rather than a negative one.
    public static int? InclusiveDays(DateOnly? startDate, DateOnly? endDate)
    {
        if (startDate is not { } start)
        {
            return null;
        }

        var end = endDate ?? start;
        return end >= start ? end.DayNumber - start.DayNumber + 1 : null;
    }
}
