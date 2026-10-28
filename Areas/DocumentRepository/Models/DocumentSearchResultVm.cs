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
    public string? ReceivedFrom { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public int OfficeCategoryId { get; set; }
    public int DocumentCategoryId { get; set; }
    public string? OfficeCategoryName { get; set; }
    public string? DocumentCategoryName { get; set; }
    public IReadOnlyCollection<string> Tags { get; set; } = Array.Empty<string>();

    // SECTION: File
    public long FileSizeBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    // SECTION: OCR metadata
    public DocOcrStatus OcrStatus { get; set; }
    public string? OcrFailureReason { get; set; }

    // SECTION: Search metadata
    public double? Rank { get; set; }
    public string? Snippet { get; set; }
    public bool MatchedInSubject { get; set; }
    public bool MatchedInTags { get; set; }
    public bool MatchedInBody { get; set; }
    // Page of the first place the search's first word appears in the text; null when unknown
    // (the text has no page breaks, or the word only matched in another form).
    public int? MatchPage { get; set; }

    // SECTION: Status
    public bool IsActive { get; set; }

    // SECTION: Personalization
    public bool IsFavourite { get; set; }

    // SECTION: AOTS visibility
    public bool IsAots { get; set; }
    public bool IsAotsUnread { get; set; }
}
