using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Visits;

// The fields of a visit, shared by Record (New) and Edit so both forms ask the same
// questions in the same order with the same rules. Rendered by _VisitFields.cshtml.
public class VisitFormInput
{
    [Required(ErrorMessage = "Enter the date of the visit.")]
    [DataType(DataType.Date)]
    [Display(Name = "Date of visit")]
    public DateOnly? DateOfVisit { get; set; }

    [Required(ErrorMessage = "Choose a visit type.")]
    [Display(Name = "Visit type")]
    public Guid? VisitTypeId { get; set; }

    [Required(ErrorMessage = "Enter who visited.")]
    [StringLength(200, ErrorMessage = "Keep the visitor to 200 characters.")]
    [Display(Name = "Visitor, course or delegation")]
    public string VisitorName { get; set; } = string.Empty;

    [Display(Name = "Number of visitors")]
    [Range(1, 100000, ErrorMessage = "Enter at least one visitor.")]
    public int Strength { get; set; } = 1;

    [StringLength(2000, ErrorMessage = "Keep the remarks to 2,000 characters.")]
    [Display(Name = "Remarks")]
    public string? Remarks { get; set; }
}
