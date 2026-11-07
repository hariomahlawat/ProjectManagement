namespace ProjectManagement.Models;

/// <summary>
/// The summary of one "Copy PDFs into the repository" run. Kept in the database so every
/// application process shows the same history and it survives a restart
/// (docs/ux/admin-review-3-fixes.md, C8). Only the newest runs are kept.
/// </summary>
public class PdfIngestionRun
{
    public Guid Id { get; set; }

    /// <summary>UTC.</summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CompletedAtUtc { get; set; }

    public string? InitiatedBy { get; set; }

    public string Status { get; set; } = string.Empty;

    public int Discovered { get; set; }

    public int IngestedOrLinked { get; set; }

    public int AlreadyLinked { get; set; }

    public int Missing { get; set; }

    public int Failed { get; set; }

    /// <summary>The per-source counts, as JSON.</summary>
    public string SourcesJson { get; set; } = "[]";

    /// <summary>Up to 50 failed items, as JSON. The failure CSV is built from these.</summary>
    public string FailuresJson { get; set; } = "[]";

    public string? TraceId { get; set; }
}
