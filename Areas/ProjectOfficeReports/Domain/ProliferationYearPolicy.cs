using System;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public static class ProliferationYearPolicy
{
    public const int MinimumYear = 2000;

    public static int GetMaximumYear(DateTimeOffset now) => now.UtcDateTime.Year + 1;

    public static bool IsValid(int year, DateTimeOffset now)
        => year >= MinimumYear && year <= GetMaximumYear(now);
}
