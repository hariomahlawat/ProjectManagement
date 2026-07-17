using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;

public sealed partial class IndexModel
{
    public sealed class YearlyRow
    {
        public int Year { get; set; }
        public int Filed { get; set; }
        public int Granted { get; set; }
    }

    public List<YearlyRow> YearlyStats { get; set; } = new();

    public sealed record TypeBreakdownRow(string Type, int Filed, int Granted, int AwaitingGrant);

    public sealed record ProjectPickerOption(
        int Id,
        string Name,
        string? Code,
        string Lifecycle)
    {
        public string SecondaryText
            => string.Join(" · ", new[] { Code, Lifecycle }.Where(value => !string.IsNullOrWhiteSpace(value)));

        public string SearchText
            => string.Join(" ", new[] { Name, Code, Lifecycle }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public sealed record ProjectIprLinkRow(
        int Id,
        int? ProjectId,
        string ProjectName,
        string Title,
        string Type,
        string Position,
        DateTime? FiledOn,
        DateTime? GrantedOn,
        int ProjectIprCount);

    public sealed record AwaitingGrantRow(
        int Id,
        int? ProjectId,
        string ProjectName,
        string Title,
        string Type,
        DateTime? FiledOn,
        int WaitingDays);

    public sealed class RecordInput
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
    }

    public sealed class DeleteInput
    {
        [HiddenInput]
        public int Id { get; set; }

        [HiddenInput]
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class UploadAttachmentInput
    {
        [HiddenInput]
        public int? RecordId { get; set; }

        [Display(Name = "Attachment")]
        public IFormFile? File { get; set; }
    }

    public sealed class RemoveAttachmentInput
    {
        [HiddenInput]
        public int AttachmentId { get; set; }

        [HiddenInput]
        public int? RecordId { get; set; }

        [HiddenInput]
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed record AttachmentViewModel(
        int Id,
        string FileName,
        long FileSize,
        string UploadedBy,
        DateTimeOffset UploadedAtUtc,
        string RowVersion);
}
