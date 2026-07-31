namespace ProjectManagement.Configuration;

/// <summary>
/// Controls the command-conference review window without changing project lifecycle data.
/// </summary>
public sealed class ConferenceOptions
{
    public const string SectionName = "Conference";

    /// <summary>
    /// Number of calendar days for which a completed project remains available in the
    /// officer conference review. The completion day is included.
    /// </summary>
    public int CompletedProjectRetentionDays { get; set; } = 90;
}
