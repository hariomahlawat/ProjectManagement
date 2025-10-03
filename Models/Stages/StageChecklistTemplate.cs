using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.Stages;

public class StageChecklistTemplate
{
    public int Id { get; set; }

    [MaxLength(32)]
    public string Version { get; set; } = "SDD-1.0";

    [MaxLength(16)]
    public string StageCode { get; set; } = string.Empty;

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }

    public ApplicationUser? UpdatedByUser { get; set; }

    public DateTimeOffset? UpdatedOn { get; set; }

    public List<StageChecklistItemTemplate> Items { get; set; } = new();

    public List<StageChecklistAudit> AuditEntries { get; set; } = new();

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public class StageChecklistItemTemplate
{
    public int Id { get; set; }

    public int TemplateId { get; set; }

    public StageChecklistTemplate? Template { get; set; }

    [MaxLength(512)]
    public string Text { get; set; } = string.Empty;

    public int Sequence { get; set; }

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }

    public ApplicationUser? UpdatedByUser { get; set; }

    public DateTimeOffset? UpdatedOn { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public class StageChecklistAudit
{
    public int Id { get; set; }

    public int TemplateId { get; set; }

    public StageChecklistTemplate? Template { get; set; }

    public int? ItemId { get; set; }

    public StageChecklistItemTemplate? Item { get; set; }

    [MaxLength(32)]
    public string Action { get; set; } = string.Empty;

    public string? PayloadJson { get; set; }

    [MaxLength(450)]
    public string? PerformedByUserId { get; set; }

    public DateTimeOffset PerformedOn { get; set; }
}
