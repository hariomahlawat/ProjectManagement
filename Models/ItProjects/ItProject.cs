using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models.ItProjects;

/// <summary>
/// An IT ARPPP case of the organisation's own: a small procurement project that is tracked by
/// the stage it has reached, not by a plan. Deliberately separate from <see cref="Project"/>
/// (docs/ux/it-projects.md).
/// </summary>
public class ItProject
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Free text, not unique, not a case file number.</summary>
    [Required, MaxLength(100)]
    public string PppReferenceNumber { get; set; } = string.Empty;

    /// <summary>Start year of the financial year (April to March), e.g. 2026 for 2026-27.</summary>
    public int FinancialYearStart { get; set; }

    [Required]
    public string Description { get; set; } = string.Empty;

    [Required, MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// When a stage save last changed the current stage (docs/ux/it-projects-redesign.md). Only a
    /// fallback for the "days at stage" figure when the previous stage has no completion date;
    /// null for projects whose stage has not moved since the column was added.
    /// </summary>
    public DateTime? StageMovedAtUtc { get; set; }

    public DateOnly? DroppedOn { get; set; }

    [MaxLength(500)]
    public string? DroppedReason { get; set; }

    [MaxLength(450)]
    public string? DroppedByUserId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    [MaxLength(450)]
    public string? DeletedByUserId { get; set; }

    [ConcurrencyCheck]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<ItProjectStage> Stages { get; set; } = new List<ItProjectStage>();

    /// <summary>
    /// Later financial years the project was carried into: that year's IT ARPPP reference number and
    /// any type set by hand. The project's own year is not stored here.
    /// </summary>
    public ICollection<ItProjectFinancialYear> FinancialYears { get; set; } = new List<ItProjectFinancialYear>();
}
