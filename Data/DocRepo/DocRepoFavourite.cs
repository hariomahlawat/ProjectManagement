using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Data.DocRepo;

// SECTION: Document repository favourites
public class DocRepoFavourite
{
    public long Id { get; set; }

    [Required, MaxLength(64)]
    public string UserId { get; set; } = null!;

    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }
}
