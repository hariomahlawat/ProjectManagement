using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models.Arpp;

public sealed class ArppDfpdsSchedule
{
    public int Id { get; set; }

    [Required]
    [MaxLength(120)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string NormalizedCode { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    [Required]
    [MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(450)]
    public string UpdatedByUserId { get; set; } = string.Empty;

    [ConcurrencyCheck]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<ArppEntry> Entries { get; set; } = new List<ArppEntry>();
}
