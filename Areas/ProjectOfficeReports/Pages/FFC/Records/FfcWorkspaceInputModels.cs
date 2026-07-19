using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using ProjectManagement.Services.Ffc;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Records;

public sealed class FfcRecordEditorInput
{
    public long? Id { get; set; }

    [Range(1, long.MaxValue, ErrorMessage = "Select a country.")]
    public long CountryId { get; set; }

    [Range(2000, 2100, ErrorMessage = "Enter a year between 2000 and 2100.")]
    public short Year { get; set; }

    public bool IpaCompleted { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? IpaDate { get; set; }

    [StringLength(4000)]
    public string? IpaRemarks { get; set; }

    public bool GslCompleted { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? GslDate { get; set; }

    [StringLength(4000)]
    public string? GslRemarks { get; set; }

    [StringLength(4000)]
    public string? OverallRemarks { get; set; }

    public string? RowVersion { get; set; }
}

public sealed class FfcProjectEditorInput
{
    public long? Id { get; set; }

    public bool IsLinkedProject { get; set; } = true;

    [StringLength(256)]
    public string? DisplayName { get; set; }

    public int? LinkedProjectId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")]
    public int Quantity { get; set; } = 1;

    public FfcUnitPosition Position { get; set; } = FfcUnitPosition.Planned;

    [DataType(DataType.Date)]
    public DateOnly? DeliveredOn { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? InstalledOn { get; set; }

    [StringLength(FfcProgressService.MaxProgressLength)]
    public string? ProgressText { get; set; }

    public string? RowVersion { get; set; }
}

public sealed class FfcAttachmentEditorInput
{
    public IFormFile? UploadFile { get; set; }

    [StringLength(256)]
    public string? Caption { get; set; }
}
