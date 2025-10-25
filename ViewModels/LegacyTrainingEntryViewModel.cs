using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ProjectManagement.ViewModels;

/// <summary>
/// Describes the fields required to capture a legacy training entry where only aggregate counts are retained.
/// </summary>
public sealed class LegacyTrainingEntryViewModel
{
    [Display(Name = "Legacy record")]
    public bool IsLegacyRecord { get; set; } = true;

    [Required]
    [Display(Name = "Training type")]
    public Guid? TrainingTypeId { get; set; }

    [Display(Name = "Schedule mode")]
    public LegacyScheduleMode ScheduleMode { get; set; } = LegacyScheduleMode.DateRange;

    [DataType(DataType.Date)]
    [Display(Name = "Start date")]
    public DateOnly? StartDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "End date")]
    public DateOnly? EndDate { get; set; }

    [Range(1, 12)]
    [Display(Name = "Month")]
    public int? TrainingMonth { get; set; }

    [Range(2000, 2100)]
    [Display(Name = "Year")]
    public int? TrainingYear { get; set; }

    [Range(0, int.MaxValue)]
    [Display(Name = "Officers")]
    public int LegacyOfficerCount { get; set; }

    [Range(0, int.MaxValue)]
    [Display(Name = "JCOs")]
    public int LegacyJcoCount { get; set; }

    [Range(0, int.MaxValue)]
    [Display(Name = "ORs")]
    public int LegacyOrCount { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public IList<int> ProjectIds { get; set; } = new List<int>();

    public IReadOnlyList<SelectListItem> TrainingTypes { get; init; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> ProjectOptions { get; init; } = Array.Empty<SelectListItem>();

    public enum LegacyScheduleMode
    {
        DateRange = 0,
        MonthAndYear = 1
    }
}
