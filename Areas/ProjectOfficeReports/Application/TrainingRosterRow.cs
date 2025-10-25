namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class TrainingRosterRow
{
    public int? Id { get; set; }

    public string? ArmyNumber { get; set; }

    public string Rank { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string UnitName { get; set; } = string.Empty;

    public byte Category { get; set; }
}
