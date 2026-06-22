using ProjectManagement.ViewModels.Notebook;

namespace ProjectManagement.Services.Notebook;

// SECTION: Notebook optimistic concurrency exception
public sealed class NotebookConcurrencyException : Exception
{
    public NotebookConcurrencyException(Guid itemId, Guid expectedVersion, Guid currentVersion)
        : this(itemId, expectedVersion, currentVersion, null, null)
    {
    }

    public NotebookConcurrencyException(Guid itemId, Guid expectedVersion, Guid currentVersion, Exception? innerException)
        : this(itemId, expectedVersion, currentVersion, null, innerException)
    {
    }

    public NotebookConcurrencyException(
        Guid itemId,
        Guid expectedVersion,
        Guid currentVersion,
        NotebookItemDetailVm? currentItem,
        Exception? innerException = null)
        : base("The notebook item version no longer matches the submitted version.", innerException)
    {
        ItemId = itemId;
        ExpectedVersion = expectedVersion;
        CurrentVersion = currentVersion;
        CurrentItem = currentItem;
    }

    public Guid ItemId { get; }

    public Guid ExpectedVersion { get; }

    public Guid CurrentVersion { get; }

    public NotebookItemDetailVm? CurrentItem { get; }
}
