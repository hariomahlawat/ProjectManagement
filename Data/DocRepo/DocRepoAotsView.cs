using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Data.DocRepo;

// SECTION: AOTS view tracking entity
public class DocRepoAotsView
{
    public long Id { get; set; }

    [Required]
    public Guid DocumentId { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    public DateTime FirstViewedAtUtc { get; set; }

    public Document? Document { get; set; }
}
