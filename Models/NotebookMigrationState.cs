using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models;

/// <summary>
/// Durable marker for one-time Notebook data migrations that may also be
/// invoked safely by application code as a fallback on older installations.
/// </summary>
public sealed class NotebookMigrationState
{
    [Required, MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    [Required, MaxLength(80)]
    public string MigrationKey { get; set; } = string.Empty;

    public DateTimeOffset CompletedAtUtc { get; set; }

    public int ImportedCount { get; set; }
}
