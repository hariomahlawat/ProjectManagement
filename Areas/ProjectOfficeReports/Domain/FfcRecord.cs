namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class FfcRecord
{
    public long Id { get; set; }
    public long CountryId { get; set; }
    public short Year { get; set; }

    public bool IpaYes { get; set; }
    public DateOnly? IpaDate { get; set; }
    public string? IpaRemarks { get; set; }

    public bool GslYes { get; set; }
    public DateOnly? GslDate { get; set; }
    public string? GslRemarks { get; set; }

    public bool DeliveryYes { get; set; }
    public DateOnly? DeliveryDate { get; set; }
    public string? DeliveryRemarks { get; set; }

    public bool InstallationYes { get; set; }
    public DateOnly? InstallationDate { get; set; }
    public string? InstallationRemarks { get; set; }

    public string? OverallRemarks { get; set; }
    public bool IsDeleted { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public FfcCountry Country { get; set; } = null!;
    public ICollection<FfcProject> Projects { get; set; } = new List<FfcProject>();
    public ICollection<FfcAttachment> Attachments { get; set; } = new List<FfcAttachment>();
}
