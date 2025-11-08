using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Data.DocRepo;

// SECTION: Document OCR text storage model
public class DocumentText
{
    [Key]
    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    // SECTION: OCR payload
    public string? OcrText { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
