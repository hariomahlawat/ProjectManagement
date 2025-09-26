using System.Collections.Generic;

namespace ProjectManagement.ViewModels;

public sealed class PlanEditVm
{
    public int ProjectId { get; set; }
    public List<PlanEditRow> Rows { get; set; } = new();

    public sealed class PlanEditRow
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateOnly? PlannedStart { get; set; }
        public DateOnly? PlannedDue { get; set; }
    }
}
