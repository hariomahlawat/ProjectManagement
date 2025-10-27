using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Areas.ProjectOfficeReports.Application;

namespace ProjectManagement.Areas.ProjectOfficeReports.MiscActivities.ViewModels;

public sealed class MiscActivityIndexFilterViewModel : IValidatableObject
{
    [Display(Name = "Activity type")]
    public Guid? ActivityTypeId { get; set; }

    [Display(Name = "From date")]
    [DataType(DataType.Date)]
    public DateOnly? StartDate { get; set; }

    [Display(Name = "To date")]
    [DataType(DataType.Date)]
    public DateOnly? EndDate { get; set; }

    [Display(Name = "Search")]
    [StringLength(256, ErrorMessage = "Search term must be 256 characters or fewer.")]
    public string? Search { get; set; }

    [Display(Name = "Include deleted activities")]
    public bool IncludeDeleted { get; set; }

    [Display(Name = "Sort by")]
    public MiscActivitySortField Sort { get; set; } = MiscActivitySortField.OccurrenceDate;

    [Display(Name = "Sort order descending")]
    public bool SortDescending { get; set; } = true;

    [Display(Name = "Captured by")]
    public string? CreatorUserId { get; set; }

    [Display(Name = "Attachment type")]
    public MiscActivityAttachmentTypeFilter AttachmentType { get; set; } = MiscActivityAttachmentTypeFilter.Any;

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 25;

    public IReadOnlyList<SelectListItem> ActivityTypeOptions { get; set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> CreatorOptions { get; set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> AttachmentTypeOptions { get; set; } = Array.Empty<SelectListItem>();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDate.HasValue && EndDate.HasValue && StartDate > EndDate)
        {
            yield return new ValidationResult(
                "Start date must be on or before the end date.",
                new[] { nameof(StartDate), nameof(EndDate) });
        }

        if (PageNumber < 1)
        {
            yield return new ValidationResult(
                "Page number must be at least 1.",
                new[] { nameof(PageNumber) });
        }

        if (PageSize < 1)
        {
            yield return new ValidationResult(
                "Page size must be at least 1.",
                new[] { nameof(PageSize) });
        }
    }

    public MiscActivityQueryOptions ToQueryOptions()
    {
        var trimmedSearch = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
        return new MiscActivityQueryOptions(
            ActivityTypeId,
            StartDate,
            EndDate,
            trimmedSearch,
            IncludeDeleted,
            Sort,
            SortDescending,
            CreatorUserId,
            AttachmentType,
            PageNumber,
            PageSize);
    }
}
