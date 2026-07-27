using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models.Arpp;

public sealed class ArppFundOption
{
    public int Id { get; set; }

    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string NormalizedName { get; set; } = string.Empty;

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
