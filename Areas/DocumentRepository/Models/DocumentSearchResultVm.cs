using System;
using System.Collections.Generic;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Models;

// SECTION: Document search result view model
public sealed class DocumentSearchResultVm
{
    // SECTION: Identity
    public Guid Id { get; set; }

    // SECTION: Primary metadata
    public string Subject { get; set; } = string.Empty;
    public DateOnly? DocumentDate { get; set; }
    public string? OfficeCategoryName { get; set; }
    public string? DocumentCategoryName { get; set; }
    public IReadOnlyCollection<string> Tags { get; set; } = Array.Empty<string>();

    // SECTION: OCR metadata
    public DocOcrStatus OcrStatus { get; set; }
    public string? OcrFailureReason { get; set; }

    // SECTION: Search metadata
    public double? Rank { get; set; }
    public string? Snippet { get; set; }
    public bool MatchedInSubject { get; set; }
    public bool MatchedInTags { get; set; }
    public bool MatchedInBody { get; set; }
}
