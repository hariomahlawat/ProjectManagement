using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models.IndustryPartners;

public class IndustryPartnerContact
{
    public int Id { get; set; }
    public int IndustryPartnerId { get; set; }
    public IndustryPartner IndustryPartner { get; set; } = null!;
    [MaxLength(200)] public string? Name { get; set; }
    [MaxLength(64)] public string? Phone { get; set; }
    [MaxLength(256)] public string? Email { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
