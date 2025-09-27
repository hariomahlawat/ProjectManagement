using System;
using System.Collections.Generic;
using ProjectManagement.Models.Scheduling;

namespace ProjectManagement.ViewModels;

public sealed class PlanDurationVm
{
    public int ProjectId { get; set; }
    public DateOnly? AnchorStart { get; set; }
    public bool IncludeWeekends { get; set; }
    public bool SkipHolidays { get; set; } = true;
    public string NextStageStartPolicy { get; set; } = NextStageStartPolicies.NextWorkingDay;
    public List<PlanDurationRowVm> Rows { get; set; } = new();
}

public sealed class PlanDurationRowVm
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? DurationDays { get; set; }
}
