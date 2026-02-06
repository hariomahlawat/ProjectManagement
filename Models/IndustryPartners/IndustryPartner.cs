using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models.IndustryPartners;

public class IndustryPartner
{
    public int Id { get; set; }
    [Required][MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required][MaxLength(200)] public string NormalizedName { get; set; } = string.Empty;
    [MaxLength(2000)] public string? Location { get; set; }
    [MaxLength(2000)] public string? NormalizedLocation { get; set; }
    [MaxLength(4000)] public string? Remarks { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    [Required][MaxLength(450)] public string CreatedByUserId { get; set; } = string.Empty;
    public DateTimeOffset? UpdatedUtc { get; set; }
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public ICollection<IndustryPartnerContact> Contacts { get; set; } = new List<IndustryPartnerContact>();
    public ICollection<IndustryPartnerAttachment> Attachments { get; set; } = new List<IndustryPartnerAttachment>();
    public ICollection<IndustryPartnerProject> PartnerProjects { get; set; } = new List<IndustryPartnerProject>();
}
