using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class MiscActivity
{
    public Guid Id { get; set; }

    public Guid? ActivityTypeId { get; set; }

    public ActivityType? ActivityType { get; set; }

    [Required]
    [MaxLength(256)]
    public string Nomenclature { get; set; } = string.Empty;

    [Required]
    public DateOnly OccurrenceDate { get; set; }

    [MaxLength(4000)]
    public string? Description { get; set; }

    [MaxLength(1024)]
    public string? ExternalLink { get; set; }

    [Required]
    [MaxLength(450)]
    public string CapturedByUserId { get; set; } = string.Empty;

    public DateTimeOffset CapturedAtUtc { get; set; }

    [MaxLength(450)]
    public string? LastModifiedByUserId { get; set; }

    public DateTimeOffset? LastModifiedAtUtc { get; set; }

    public DateTimeOffset? DeletedUtc { get; set; }

    [MaxLength(450)]
    public string? DeletedByUserId { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<ActivityMedia> Media { get; set; } = new HashSet<ActivityMedia>();
}
