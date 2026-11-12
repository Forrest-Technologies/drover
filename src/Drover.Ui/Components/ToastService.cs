namespace Drover.Ui.Components;

/// <summary>
/// Holds the toasts on screen. Anything can show one; <see cref="ToastHost"/> renders them.
/// A toast stays until the person dismisses it, so nothing is lost to a timer.
/// </summary>
public sealed class ToastService
{
    #region Private Fields

    private readonly List<ToastMessage> toasts = [];

    private long lastId;

    #endregion

    #region Properties

    public IReadOnlyList<ToastMessage> Toasts => toasts;

    public event Action? Changed;

    #endregion

    #region Public Methods

    public void Show(string message, ToastKind kind = ToastKind.Info)
    {
        toasts.Add(new(++lastId, message, kind));
        Changed?.Invoke();
    }

    public void Dismiss(long id)
    {
        if (toasts.RemoveAll(toast => toast.Id == id) > 0)
        {
            Changed?.Invoke();
        }
    }

    #endregion
}
