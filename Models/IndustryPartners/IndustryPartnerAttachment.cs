using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models.IndustryPartners;

public class IndustryPartnerAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int IndustryPartnerId { get; set; }
    public IndustryPartner IndustryPartner { get; set; } = null!;
    [Required][MaxLength(260)] public string OriginalFileName { get; set; } = string.Empty;
    [Required][MaxLength(260)] public string StorageKey { get; set; } = string.Empty;
    [Required][MaxLength(128)] public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    [Required][MaxLength(64)] public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset UploadedUtc { get; set; } = DateTimeOffset.UtcNow;
    [Required][MaxLength(450)] public string UploadedByUserId { get; set; } = string.Empty;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
