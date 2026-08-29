using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using ProjectManagement.Utilities;

namespace ProjectManagement.ViewModels;

public sealed class ProjectProliferationProfileVm
{
    public static ProjectProliferationProfileVm Empty(int projectId) => new() { ProjectId = projectId };

    public int ProjectId { get; init; }
    public decimal? CostLakhs { get; init; }
    public bool? AvailableForProliferation { get; init; }
    public string? NotAvailableReason { get; init; }
    public string? Remarks { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public string? UpdatedByDisplayName { get; init; }

    public string CostDisplay => FormatCost(CostLakhs);

    public string AvailabilityDisplay => AvailableForProliferation switch
    {
        true => "Available for proliferation",
        false => "Not available for proliferation",
        _ => "Availability not assessed"
    };

    public string AvailabilityTone => AvailableForProliferation switch
    {
        true => "positive",
        false => "negative",
        _ => "neutral"
    };

    public string UpdatedDisplay
    {
        get
        {
            if (!UpdatedAtUtc.HasValue)
            {
                return "No proliferation update recorded";
            }

            var local = TimeZoneInfo.ConvertTime(UpdatedAtUtc.Value, TimeZoneHelper.GetIst());
            var date = local.ToString("dd MMM yyyy, HH:mm 'IST'", CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(UpdatedByDisplayName)
                ? $"Last updated {date}"
                : $"Last updated {date} by {UpdatedByDisplayName}";
        }
    }

    public static string FormatCost(decimal? costLakhs)
    {
        if (!costLakhs.HasValue)
        {
            return "Cost not recorded";
        }

        if (costLakhs.Value >= 100m)
        {
            return $"₹{costLakhs.Value / 100m:0.##} Cr";
        }

        return $"₹{costLakhs.Value:0.##} lakh";
    }
}

public sealed class ProjectProliferationUpdateInput
{
    public int ProjectId { get; set; }

    [Display(Name = "Indicative proliferation cost")]
    [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "Proliferation cost cannot be negative.")]
    public decimal? CostLakhs { get; set; }

    [Display(Name = "Availability for proliferation")]
    public bool? AvailableForProliferation { get; set; }

    [Display(Name = "Reason not available")]
    [StringLength(500)]
    public string? NotAvailableReason { get; set; }

    [Display(Name = "Proliferation remarks")]
    [StringLength(500)]
    public string? Remarks { get; set; }
}
