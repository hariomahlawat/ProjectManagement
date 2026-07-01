namespace ProjectManagement.Configuration;

/// <summary>
/// Controls deliberate retention of the primary application audit log. Retention is
/// disabled by default because audit destruction is a governance decision and must never
/// be coupled to an IIS recycle or ordinary application startup.
/// </summary>
public sealed class AuditRetentionOptions
{
    public const string SectionName = "Audit:Retention";

    public bool Enabled { get; set; }

    public int RetentionDays { get; set; } = 365;

    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromDays(1);

    public int BatchSize { get; set; } = 5_000;

    public TimeSpan GetSafeSweepInterval()
        => SweepInterval >= TimeSpan.FromMinutes(15)
            ? SweepInterval
            : TimeSpan.FromMinutes(15);

    public int GetSafeBatchSize() => Math.Clamp(BatchSize, 100, 25_000);
}
