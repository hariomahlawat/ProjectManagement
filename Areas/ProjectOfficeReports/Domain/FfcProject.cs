using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class FfcProject
{
    public long Id { get; set; }
    public long FfcRecordId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public int? LinkedProjectId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public FfcRecord Record { get; set; } = null!;
    public Project? LinkedProject { get; set; }
}
