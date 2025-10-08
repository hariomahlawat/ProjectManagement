namespace ProjectManagement.ViewModels;

public sealed class ProjectDocumentSummaryViewModel
{
    public static readonly ProjectDocumentSummaryViewModel Empty = new();

    public int TotalCount { get; init; }
    public int PublishedCount { get; init; }
    public int PendingCount { get; init; }

    public bool HasPending => PendingCount > 0;
}
