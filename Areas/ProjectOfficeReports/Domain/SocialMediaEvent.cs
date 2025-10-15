using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class SocialMediaEvent
{
    public Guid Id { get; set; }

    [Required]
    public Guid SocialMediaEventTypeId { get; set; }

    public SocialMediaEventType? SocialMediaEventType { get; set; }

    [Required]
    public DateOnly DateOfEvent { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? Platform { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    public Guid? CoverPhotoId { get; set; }

    public SocialMediaEventPhoto? CoverPhoto { get; set; }

    public ICollection<SocialMediaEventPhoto> Photos { get; set; } = new HashSet<SocialMediaEventPhoto>();

    [Required]
    [MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    [MaxLength(450)]
    public string? LastModifiedByUserId { get; set; }

    public DateTimeOffset? LastModifiedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
