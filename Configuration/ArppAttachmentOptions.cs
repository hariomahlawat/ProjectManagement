namespace ProjectManagement.Configuration;

public sealed class ArppAttachmentOptions
{
    public const string SectionName = "ArppAttachments";

    public long MaxFileSizeBytes { get; set; } = 100L * 1024L * 1024L;

    public string StorageFolderName { get; set; } = "arpp";

    public bool IngestIntoDocumentRepository { get; set; } = true;
}
