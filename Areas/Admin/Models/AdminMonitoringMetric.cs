namespace ProjectManagement.Areas.Admin.Models;

public sealed record AdminMonitoringMetricModel
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public string Icon { get; init; } = "bi-activity";
    public string Tone { get; init; } = "neutral";
    public string? Href { get; init; }
    public bool IsActive { get; init; }
}
