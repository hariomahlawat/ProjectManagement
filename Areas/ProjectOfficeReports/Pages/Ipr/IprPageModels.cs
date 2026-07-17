using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;

public sealed class IprRecordFormModel
{
    [HiddenInput]
    public int? Id { get; set; }

    [Display(Name = "Filing number")]
    [Required]
    [StringLength(128)]
    public string? FilingNumber { get; set; }

    [Display(Name = "Title")]
    [Required]
    [StringLength(256)]
    public string? Title { get; set; }

    [Display(Name = "Notes")]
    [StringLength(2000)]
    public string? Notes { get; set; }

    [Display(Name = "Type")]
    [Required]
    public IprType? Type { get; set; }

    [Display(Name = "Status")]
    [Required]
    public IprStatus? Status { get; set; }

    [Display(Name = "Filed by")]
    [StringLength(128)]
    public string? FiledBy { get; set; }

    [Display(Name = "Filed on")]
    [Required]
    [DataType(DataType.Date)]
    public DateOnly? FiledOn { get; set; }

    [Display(Name = "Granted on")]
    [DataType(DataType.Date)]
    public DateOnly? GrantedOn { get; set; }

    [Display(Name = "Project")]
    public int? ProjectId { get; set; }

    public string? RowVersion { get; set; }

    public static IprRecordFormModel CreateDefault()
        => new()
        {
            Type = IprType.Patent,
            Status = IprStatus.Filed
        };
}

public sealed class IprDeleteRequest
{
    public int Id { get; set; }

    public string? RowVersion { get; set; }
}

public sealed class IprAttachmentUploadRequest
{
    public int? RecordId { get; set; }

    [Display(Name = "Attachment")]
    public IFormFile? File { get; set; }
}

public sealed class IprAttachmentRemoveRequest
{
    public int AttachmentId { get; set; }

    public int? RecordId { get; set; }

    public string? RowVersion { get; set; }
}

public sealed record IprProjectOption(
    int Id,
    string Name,
    string? CaseFileNumber,
    string Status,
    bool IsDeleted)
{
    public string NativeLabel
        => string.IsNullOrWhiteSpace(CaseFileNumber)
            ? Name
            : $"{Name} ({CaseFileNumber})";

    public string SearchTerms
        => string.Join(' ', new[] { Name, CaseFileNumber, Status }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed class IprYearlyRow
{
    public int Year { get; set; }

    public int Filed { get; set; }

    public int Granted { get; set; }
}

public sealed record IprTypeBreakdownRow(
    string Type,
    int Filed,
    int Granted,
    int AwaitingGrant);

public sealed record IprProjectLinkRow(
    int Id,
    int? ProjectId,
    string ProjectName,
    string Title,
    string Type,
    string Position,
    DateTime? FiledOn,
    DateTime? GrantedOn,
    int ProjectIprCount);

public sealed record IprAwaitingGrantRow(
    int Id,
    int? ProjectId,
    string ProjectName,
    string Title,
    string Type,
    DateTime? FiledOn,
    int WaitingDays);

public sealed record IprAttachmentViewModel(
    int Id,
    string FileName,
    long FileSize,
    string UploadedBy,
    DateTimeOffset UploadedAtUtc,
    string RowVersion);