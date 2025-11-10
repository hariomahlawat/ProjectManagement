using System;

namespace ProjectManagement.Data.DocRepo;

// SECTION: External link entity
public class DocRepoExternalLink
{
    // SECTION: Identity
    public Guid Id { get; set; }

    // SECTION: Relationship
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    // SECTION: Source metadata
    public string SourceModule { get; set; } = null!;
    public string SourceItemId { get; set; } = null!;

    // SECTION: Audit metadata
    public DateTimeOffset CreatedAtUtc { get; set; }
}
