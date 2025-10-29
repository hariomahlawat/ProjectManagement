namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public enum FfcAttachmentKind
{
    Pdf,
    Photo
}

public class FfcAttachment
{
    public long Id { get; set; }
    public long FfcRecordId { get; set; }
    public FfcAttachmentKind Kind { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? ChecksumSha256 { get; set; }
    public string? Caption { get; set; }
    public string? UploadedByUserId { get; set; }
    public DateTimeOffset UploadedAt { get; set; }

    public FfcRecord Record { get; set; } = null!;
}
