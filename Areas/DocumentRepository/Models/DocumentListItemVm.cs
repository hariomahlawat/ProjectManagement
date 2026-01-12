using System;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Models;

// SECTION: Document list item view model
public sealed class DocumentListItemVm
{
    // SECTION: Identity
    public Guid Id { get; set; }

    // SECTION: Primary metadata
    public string Subject { get; set; } = string.Empty;
    public string? OfficeName { get; set; }
    public string? DocumentCategoryName { get; set; }
    public DateOnly? DocumentDate { get; set; }

    // SECTION: Status metadata
    public DocOcrStatus OcrStatus { get; set; }
    public bool IsActive { get; set; }

    // SECTION: Personalization
    public bool IsFavourite { get; set; }
}
