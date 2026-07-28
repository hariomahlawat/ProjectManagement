namespace ProjectManagement.Configuration;

public sealed class ProliferationExportOptions
{
    public const string SectionName = "Proliferation:Export";

    public int MaximumProjectRows { get; set; } = 5_000;

    public int MaximumUnitRows { get; set; } = 50_000;

    public int TimeoutSeconds { get; set; } = 120;
}
