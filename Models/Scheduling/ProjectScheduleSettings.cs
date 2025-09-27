using System;
using ProjectManagement.Models;

namespace ProjectManagement.Models.Scheduling;

public class ProjectScheduleSettings
{
    public int ProjectId { get; set; }
    public bool IncludeWeekends { get; set; }
    public bool SkipHolidays { get; set; } = true;
    public string NextStageStartPolicy { get; set; } = NextStageStartPolicies.NextWorkingDay;
    public DateOnly? AnchorStart { get; set; }

    public Project Project { get; set; } = default!;
}
