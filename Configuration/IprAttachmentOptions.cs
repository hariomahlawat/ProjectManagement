using System.Collections.Generic;

namespace ProjectManagement.Configuration;

public sealed class IprAttachmentOptions
{
    // SECTION: Storage settings
    public string StorageFolderName { get; set; } = "ipr-attachments";

    public string? StorageRoot { get; set; }

    // SECTION: Content validation
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;

    public IList<string> AllowedContentTypes { get; set; } = new List<string>
    {
        "application/pdf"
    };
}
