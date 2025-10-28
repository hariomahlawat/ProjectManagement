using System;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.Activities;

public class ActivityDeleteRequest
{
    public int Id { get; set; }

    [Required]
    public int ActivityId { get; set; }

    public Activity Activity { get; set; } = null!;

    [Required]
    [MaxLength(450)]
    public string RequestedByUserId { get; set; } = string.Empty;

    public ApplicationUser? RequestedByUser { get; set; }

    public DateTimeOffset RequestedAtUtc { get; set; }

    [MaxLength(450)]
    public string? ApprovedByUserId { get; set; }

    public ApplicationUser? ApprovedByUser { get; set; }

    public DateTimeOffset? ApprovedAtUtc { get; set; }

    [MaxLength(450)]
    public string? RejectedByUserId { get; set; }

    public ApplicationUser? RejectedByUser { get; set; }

    public DateTimeOffset? RejectedAtUtc { get; set; }

    [MaxLength(1000)]
    public string? Reason { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
