using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Infrastructure.Data;

public enum IprType
{
    Patent = 1,
    Copyright = 2,
    Trademark = 3,
    Design = 4,
    TradeSecret = 5
}

public enum IprStatus
{
    Draft = 1,
    Filed = 2,
    Granted = 3,
    Rejected = 4,
    Expired = 5
}

public class IprRecord
{
    public int Id { get; set; }

    [Required]
    [MaxLength(128)]
    public string IprFilingNumber { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Title { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    [Required]
    public IprType Type { get; set; } = IprType.Patent;

    [Required]
    public IprStatus Status { get; set; } = IprStatus.Draft;

    public DateTimeOffset? FiledAtUtc { get; set; }

    public int? ProjectId { get; set; }

    public Project? Project { get; set; }

    public ICollection<IprAttachment> Attachments { get; set; } = new List<IprAttachment>();

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
