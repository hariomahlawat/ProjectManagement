namespace ProjectManagement.Areas.Admin.Models;

public sealed record AdminMetricCardModel
{
    public string Eyebrow { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string? Icon { get; init; }

    public string Tone { get; init; } = "neutral";

    public string? Href { get; init; }

    public string LinkText { get; init; } = "Open";

    public IReadOnlyList<AdminMetricDetailModel> Details { get; init; } =
        Array.Empty<AdminMetricDetailModel>();
}

public sealed record AdminMetricDetailModel(string Label, string Value, string? Tone = null);
