using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Areas.ProjectOfficeReports.SocialMedia.ViewModels;

public sealed class SocialMediaEventEditModel
{
    public Guid Id { get; set; }

    [Display(Name = "Event type")]
    [Required(ErrorMessage = "Select an event type.")]
    public Guid? EventTypeId { get; set; }

    [Display(Name = "Date of event")]
    [DataType(DataType.Date)]
    [Required(ErrorMessage = "Select the event date.")]
    public DateOnly? DateOfEvent { get; set; }

    [Display(Name = "Title")]
    [Required(ErrorMessage = "Enter a title.")]
    [StringLength(200, ErrorMessage = "Title must be 200 characters or fewer.")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Platform")]
    [StringLength(128, ErrorMessage = "Platform must be 128 characters or fewer.")]
    public string? Platform { get; set; }

    [Display(Name = "Reach")]
    [Range(0, int.MaxValue, ErrorMessage = "Reach must be zero or greater.")]
    public int Reach { get; set; }

    [Display(Name = "Description")]
    [StringLength(2000, ErrorMessage = "Description must be 2000 characters or fewer.")]
    [DataType(DataType.MultilineText)]
    public string? Description { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
