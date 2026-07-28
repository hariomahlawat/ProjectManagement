namespace ProjectManagement.Configuration;

public sealed class TrainingTrackerOptions
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Maximum number of training-event rows permitted in one synchronous workbook.
    /// </summary>
    public int MaxExportTrainingRows { get; set; } = 5000;

    /// <summary>
    /// Maximum number of trainee roster rows permitted in one synchronous workbook.
    /// </summary>
    public int MaxExportRosterRows { get; set; } = 50000;

    /// <summary>
    /// Soft upper limit for the complete export operation. The service checks this
    /// before and after workbook generation and returns a controlled error when exceeded.
    /// </summary>
    public int ExportTimeoutSeconds { get; set; } = 120;
}
