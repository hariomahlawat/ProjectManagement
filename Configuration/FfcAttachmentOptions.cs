namespace ProjectManagement.Configuration;

public sealed class FfcAttachmentOptions
{
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;
    public string? StorageRoot { get; set; }
    public string StorageFolderName { get; set; } = "ffc";
}
