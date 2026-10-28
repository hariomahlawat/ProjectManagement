namespace ProjectManagement.Services.DocRepo;

public static class DocRepoAuditEventTypes
{
    // Reading. "Viewed" covers both the reader page and the inline file it embeds, de-duplicated
    // per user and document so one visit records one row.
    public const string Viewed = "Viewed";
    public const string Downloaded = "Downloaded";

    // Changes
    public const string MetadataEdited = "MetadataEdited";
    public const string OcrRequeued = "OcrRequeued";
    public const string CategoryChanged = "CategoryChanged";

    /// <summary>How long one view or download is counted as the same access.</summary>
    public static readonly TimeSpan AccessWindow = TimeSpan.FromMinutes(5);
}
