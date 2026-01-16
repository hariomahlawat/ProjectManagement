using System;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public class FfcProject
{
    public long Id { get; set; }
    public long FfcRecordId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public string? ProgressRemarks { get; set; }
    public DateTimeOffset? ProgressRemarksUpdatedAtUtc { get; set; }
    public string? ProgressRemarksUpdatedByUserId { get; set; }
    public int? LinkedProjectId { get; set; }
    public int Quantity { get; set; } = 1;
    public bool IsDelivered { get; set; }
    public DateOnly? DeliveredOn { get; set; }
    public bool IsInstalled { get; set; }
    public DateOnly? InstalledOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public FfcRecord Record { get; set; } = null!;
    public Project? LinkedProject { get; set; }
}
