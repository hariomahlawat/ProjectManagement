namespace ProjectManagement.Configuration;

public sealed class IprAttachmentOptions
{
    public string StorageFolderName { get; set; } = "ipr-attachments";

    public string? StorageRoot { get; set; }
}
