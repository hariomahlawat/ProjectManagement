using System;
using System.Collections.Generic;
using ProjectManagement.Models.Scheduling;

namespace ProjectManagement.ViewModels;

public sealed class PlanEditInput
{
    public int ProjectId { get; set; }
    public string Mode { get; set; } = PlanEditorModes.Exact;
    public string? Action { get; set; }
    public DateOnly? AnchorStart { get; set; }
    public bool IncludeWeekends { get; set; }
    public bool SkipHolidays { get; set; } = true;
    public string NextStageStartPolicy { get; set; } = NextStageStartPolicies.NextWorkingDay;
    public List<PlanEditInputRow> Rows { get; set; } = new();
}

public sealed class PlanEditInputRow
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateOnly? PlannedStart { get; set; }
    public DateOnly? PlannedDue { get; set; }
    public int? DurationDays { get; set; }
}
