using System;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Data.Projects;

// SECTION: Project document OCR text entity
public sealed class ProjectDocumentText
{
    [Key]
    public int ProjectDocumentId { get; set; }

    public ProjectDocument ProjectDocument { get; set; } = null!;

    // SECTION: OCR payload
    public string? OcrText { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
