using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models;

/// <summary>
/// Personal presentation metadata for live, system-owned Notebook surfaces.
/// The authoritative content remains in its source module; this row stores only how
/// one user wants that surface organised inside Notebook.
/// </summary>
public sealed class NotebookSystemItemPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    [Required, MaxLength(80)]
    public string SystemItemKey { get; set; } = string.Empty;

    public bool ShowInHome { get; set; }

    public bool IsPinned { get; set; }

    /// <summary>
    /// Zero-based visual slot among the user's reorderable cards in the selected
    /// Pinned/Others section. Person-shared read-only cards remain outside this order.
    /// </summary>
    public int HomePosition { get; set; }

    [MaxLength(24)]
    public string ColorKey { get; set; } = "white";

    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<NotebookSystemItemTag> Tags { get; set; } = new List<NotebookSystemItemTag>();
}

public sealed class NotebookSystemItemTag
{
    public Guid PreferenceId { get; set; }
    public NotebookSystemItemPreference? Preference { get; set; }

    public int NotebookTagId { get; set; }
    public NotebookTag? NotebookTag { get; set; }
}
