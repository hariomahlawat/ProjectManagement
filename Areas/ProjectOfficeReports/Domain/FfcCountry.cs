namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class FfcCountry
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? IsoCode { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<FfcRecord> Records { get; set; } = new List<FfcRecord>();
}
