namespace ProjectManagement.Configuration;

/// <summary>
/// Controls the administrative recovery experience without changing the
/// underlying domain retention policies.
/// </summary>
public sealed class AdminRecoveryOptions
{
    public const string SectionName = "AdminRecovery";

    public int DueSoonDays { get; set; } = 7;

    public int DefaultPageSize { get; set; } = 25;

    public int MaximumPageSize { get; set; } = 100;

    public int MaximumBulkDocuments { get; set; } = 100;

    public int RecentOperationCount { get; set; } = 8;

    public int LegacyImportPreviewRows { get; set; } = 100;

    public int LegacyImportStagingMinutes { get; set; } = 30;
}
