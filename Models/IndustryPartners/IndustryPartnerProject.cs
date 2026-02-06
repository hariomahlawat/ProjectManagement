using System;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.IndustryPartners;

public class IndustryPartnerProject
{
    public int IndustryPartnerId { get; set; }
    public IndustryPartner IndustryPartner { get; set; } = null!;
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public DateTimeOffset LinkedUtc { get; set; } = DateTimeOffset.UtcNow;
    [Required][MaxLength(450)] public string LinkedByUserId { get; set; } = string.Empty;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
