using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.Partners;

// SECTION: Industry partner master
public class IndustryPartner
{
    public int Id { get; set; }

    [Required]
    [MaxLength(256)]
    public string FirmName { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string NormalizedFirmName { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? PartnerType { get; set; }

    [MaxLength(512)]
    public string? AddressText { get; set; }

    [MaxLength(120)]
    public string? City { get; set; }

    [MaxLength(120)]
    public string? State { get; set; }

    [MaxLength(20)]
    public string? Pincode { get; set; }

    [MaxLength(256)]
    public string? Website { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    [Required]
    [MaxLength(40)]
    public string Status { get; set; } = IndustryPartnerStatuses.Active;

    [Required]
    [MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<IndustryPartnerContact> Contacts { get; set; } = new List<IndustryPartnerContact>();

    public ICollection<IndustryPartnerAttachment> Attachments { get; set; } = new List<IndustryPartnerAttachment>();

    public ICollection<ProjectIndustryPartner> ProjectLinks { get; set; } = new List<ProjectIndustryPartner>();
}

// SECTION: Partner contacts
public class IndustryPartnerContact
{
    public int Id { get; set; }

    [Required]
    public int PartnerId { get; set; }

    public IndustryPartner Partner { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? Designation { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    public bool IsPrimary { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [Required]
    [MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<IndustryPartnerContactPhone> Phones { get; set; } = new List<IndustryPartnerContactPhone>();
}

// SECTION: Contact phone numbers
public class IndustryPartnerContactPhone
{
    public int Id { get; set; }

    [Required]
    public int ContactId { get; set; }

    public IndustryPartnerContact Contact { get; set; } = null!;

    [Required]
    [MaxLength(40)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(32)]
    public string Label { get; set; } = "Mobile";
}

// SECTION: Partner attachments
public class IndustryPartnerAttachment
{
    public int Id { get; set; }

    [Required]
    public int PartnerId { get; set; }

    public IndustryPartner Partner { get; set; } = null!;

    [Required]
    [MaxLength(260)]
    public string StorageKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string ContentType { get; set; } = string.Empty;

    public long FileSize { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(80)]
    public string? AttachmentType { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [Required]
    [MaxLength(450)]
    public string UploadedByUserId { get; set; } = string.Empty;

    public DateTime UploadedAtUtc { get; set; }
}

// SECTION: Project association join
public class ProjectIndustryPartner
{
    [Required]
    public int ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    [Required]
    public int PartnerId { get; set; }

    public IndustryPartner Partner { get; set; } = null!;

    [Required]
    [MaxLength(80)]
    public string Role { get; set; } = IndustryPartnerRoles.JointDevelopmentPartner;

    public DateOnly? FromDate { get; set; }

    public DateOnly? ToDate { get; set; }

    [Required]
    [MaxLength(40)]
    public string Status { get; set; } = IndustryPartnerAssociationStatuses.Active;

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [Required]
    [MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

// SECTION: Static lookups
public static class IndustryPartnerStatuses
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Caution = "Caution";
    public const string Blacklisted = "Blacklisted";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Active,
        Inactive,
        Caution,
        Blacklisted
    };
}

public static class IndustryPartnerTypes
{
    public const string Industry = "Industry";
    public const string Oem = "OEM";
    public const string Msme = "MSME";
    public const string Academic = "Academic";
    public const string Psu = "PSU";
    public const string Consultant = "Consultant";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Industry,
        Oem,
        Msme,
        Academic,
        Psu,
        Consultant
    };
}

public static class IndustryPartnerRoles
{
    public const string JointDevelopmentPartner = "Joint Development Partner";

    public static readonly IReadOnlyList<string> All = new[]
    {
        JointDevelopmentPartner
    };
}

public static class IndustryPartnerAssociationStatuses
{
    public const string Active = "Active";
    public const string Closed = "Closed";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Active,
        Closed
    };
}

public static class IndustryPartnerAttachmentTypes
{
    public const string VisitingCard = "Visiting Card";
    public const string Brochure = "Brochure";
    public const string Capability = "Capability";
    public const string Nda = "NDA";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All = new[]
    {
        VisitingCard,
        Brochure,
        Capability,
        Nda,
        Other
    };
}
