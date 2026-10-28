using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Models;

// SECTION: Reading-pane view model (Documents/Index preview handler)
public sealed class DocumentPreviewVm
{
    public Guid Id { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? ReceivedFrom { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public string OfficeName { get; set; } = string.Empty;
    public string DocumentCategoryName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public bool IsActive { get; set; }
    public bool IsAots { get; set; }
    public DocOcrStatus OcrStatus { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public string? CreatedByName { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
    public string ReturnUrl { get; set; } = string.Empty;
}
