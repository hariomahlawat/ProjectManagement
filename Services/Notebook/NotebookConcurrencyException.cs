namespace ProjectManagement.Services.Notebook;

// SECTION: Notebook optimistic concurrency exception
public sealed class NotebookConcurrencyException : Exception
{
    public NotebookConcurrencyException(Guid itemId, Guid expectedVersion, Guid currentVersion)
        : this(itemId, expectedVersion, currentVersion, null)
    {
    }

    public NotebookConcurrencyException(Guid itemId, Guid expectedVersion, Guid currentVersion, Exception? innerException)
        : base("The notebook item version no longer matches the submitted version.", innerException)
    {
        ItemId = itemId;
        ExpectedVersion = expectedVersion;
        CurrentVersion = currentVersion;
    }

    public Guid ItemId { get; }

    public Guid ExpectedVersion { get; }

    public Guid CurrentVersion { get; }
}
