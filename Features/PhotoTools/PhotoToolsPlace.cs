namespace ProjectManagement.Features.PhotoTools;

/// <summary>
/// A place in one of Photo Tools' server queues (Remove background, finding a face). It is taken
/// before the photo is read and given back exactly once, however often it is disposed, on every
/// way out of a request.
/// </summary>
internal sealed class PhotoToolsPlace(Action giveBack) : IDisposable
{
    private int _given;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _given, 1) == 0)
        {
            giveBack();
        }
    }
}
