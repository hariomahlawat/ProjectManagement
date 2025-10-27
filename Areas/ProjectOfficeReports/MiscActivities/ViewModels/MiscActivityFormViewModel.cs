using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ProjectManagement.Areas.ProjectOfficeReports.MiscActivities.ViewModels;

public sealed class MiscActivityFormViewModel
{
    public Guid Id { get; set; }

    [Display(Name = "Activity type")]
    public Guid? ActivityTypeId { get; set; }

    [Display(Name = "Activity date")]
    [DataType(DataType.Date)]
    [Required(ErrorMessage = "Select the activity date.")]
    public DateOnly? OccurrenceDate { get; set; }

    [Display(Name = "Nomenclature")]
    [Required(ErrorMessage = "Enter the activity nomenclature.")]
    [StringLength(256, ErrorMessage = "Nomenclature must be 256 characters or fewer.")]
    public string Nomenclature { get; set; } = string.Empty;

    [Display(Name = "Description")]
    [StringLength(4000, ErrorMessage = "Description must be 4000 characters or fewer.")]
    [DataType(DataType.MultilineText)]
    public string? Description { get; set; }

    [Display(Name = "External link")]
    [StringLength(1024, ErrorMessage = "External link must be 1024 characters or fewer.")]
    [Url(ErrorMessage = "Enter a valid URL.")]
    public string? ExternalLink { get; set; }

    public string RowVersion { get; set; } = string.Empty;

    public IReadOnlyList<SelectListItem> ActivityTypeOptions { get; set; } = Array.Empty<SelectListItem>();
}
