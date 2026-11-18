namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// The thread the interface is drawn on. A view model that hears from the server on its own loop, such as a run's
/// events or a dropped connection, tells its screen of a change through this, so the screen is only ever touched
/// from its own thread.
/// </summary>
public interface IUiThread
{
    /// <summary>Runs <paramref name="work"/> on the interface's thread: at once when already there, soon otherwise.</summary>
    void Post(Action work);
}
