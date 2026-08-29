using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Services.SearchV2;

public sealed class SearchV2Options
{
    public const string SectionName = "Search:V2";

    public bool Enabled { get; set; } = true;
    public bool ServeV2 { get; set; } = false;
    public bool ShadowMode { get; set; } = true;
    public string[] ServeV2Users { get; set; } = Array.Empty<string>();
    public string[] ServeV2Roles { get; set; } = Array.Empty<string>();

    [Range(5, 100)]
    public int PageSize { get; set; } = 20;

    [Range(5, 100)]
    public int MaxPageSize { get; set; } = 50;

    [Range(5, 20)]
    public int SuggestionLimit { get; set; } = 8;

    [Range(0.05, 0.95)]
    public double FuzzyThreshold { get; set; } = 0.28;

    [Range(1, 500)]
    public int ReciprocalRankK { get; set; } = 60;

    [Range(1, int.MaxValue)]
    public int IndexVersion { get; set; } = 2;

    [Range(5, 3600)]
    public int WorkerIntervalSeconds { get; set; } = 15;

    [Range(1, 1440)]
    public int WorkItemLeaseMinutes { get; set; } = 10;

    [Range(1, 10080)]
    public int FullReconciliationMinutes { get; set; } = 1440;

    [Range(1, 3650)]
    public int QueryLogRetentionDays { get; set; } = 90;

    [Range(120, 1200)]
    public int MaxSnippetCharacters { get; set; } = 420;
}
