using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class Visit
{
    public Guid Id { get; set; }

    [Required]
    public Guid VisitTypeId { get; set; }

    public VisitType? VisitType { get; set; }

    [Required]
    public DateOnly DateOfVisit { get; set; }

    [Required]
    [MaxLength(200)]
    public string VisitorName { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int Strength { get; set; }

    [MaxLength(2000)]
    public string? Remarks { get; set; }

    public Guid? CoverPhotoId { get; set; }

    public VisitPhoto? CoverPhoto { get; set; }

    public ICollection<VisitPhoto> Photos { get; set; } = new HashSet<VisitPhoto>();

    [Required]
    [MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    [MaxLength(450)]
    public string? LastModifiedByUserId { get; set; }

    public DateTimeOffset? LastModifiedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
