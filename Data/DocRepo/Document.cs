using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NpgsqlTypes;

namespace ProjectManagement.Data.DocRepo;

// SECTION: OCR status enumerations
public enum DocOcrStatus
{
    None = 0,
    Pending = 1,
    Succeeded = 2,
    Failed = 3
}

public class Document
{
    public Guid Id { get; set; }

    [Required, MaxLength(256)]
    public string Subject { get; set; } = null!;

    [MaxLength(256)]
    public string? ReceivedFrom { get; set; }

    public DateOnly? DocumentDate { get; set; }

    public int OfficeCategoryId { get; set; }
    public OfficeCategory OfficeCategory { get; set; } = null!;

    public int DocumentCategoryId { get; set; }
    public DocumentCategory DocumentCategory { get; set; } = null!;

    [Required, MaxLength(260)]
    public string OriginalFileName { get; set; } = null!;

    public long FileSizeBytes { get; set; }

    [Required, MaxLength(64)]
    public string Sha256 { get; set; } = null!;

    [Required, MaxLength(260)]
    public string StoragePath { get; set; } = null!;

    [Required, MaxLength(64)]
    public string MimeType { get; set; } = "application/pdf";

    public bool IsActive { get; set; } = true;

    // SECTION: External ingestion metadata
    public bool IsExternal { get; set; } = false;

    // SECTION: Soft delete metadata
    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAtUtc { get; set; }

    [MaxLength(64)]
    public string? DeletedByUserId { get; set; }

    [MaxLength(512)]
    public string? DeleteReason { get; set; }

    // SECTION: OCR metadata
    public DocOcrStatus OcrStatus { get; set; } = DocOcrStatus.None;

    [MaxLength(1024)]
    public string? OcrFailureReason { get; set; }

    public DateTimeOffset? OcrLastTriedUtc { get; set; }

    // SECTION: Full-text search support
    public NpgsqlTsVector? SearchVector { get; set; }

    public DocumentText? DocumentText { get; set; }

    [Required, MaxLength(64)]
    public string CreatedByUserId { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    [MaxLength(64)]
    public string? UpdatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<DocumentTag> DocumentTags { get; set; } = new List<DocumentTag>();

    // SECTION: External link navigation
    public ICollection<DocRepoExternalLink> ExternalLinks { get; set; } = new List<DocRepoExternalLink>();
}
