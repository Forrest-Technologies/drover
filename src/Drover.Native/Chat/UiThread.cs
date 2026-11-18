namespace Drover.Native.Chat;

/// <summary>
/// The interface's thread, for a view model that hears from the server on a loop of its own: what it shows changes
/// here and nowhere else. Work that is already on the thread runs at once, so a change a person caused is on screen
/// before their next key.
/// </summary>
internal sealed class UiThread(Microsoft.UI.Dispatching.DispatcherQueue queue) : IUiThread
{
    #region Interface Implementations

    public void Post(Action work)
    {
        if (queue.HasThreadAccess)
        {
            work();
        }
        else
        {
            queue.TryEnqueue(() => work());
        }
    }

    #endregion
}
