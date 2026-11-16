namespace Drover.Ui.Components;

/// <summary>
/// Holds the toasts on screen. Anything can show one; <see cref="ToastHost"/> renders them.
/// A toast stays until the person dismisses it, so nothing is lost to a timer.
/// </summary>
public sealed class ToastService(TimeProvider time)
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

    public void Show(string message, ToastKind kind = ToastKind.Info, ToastAction? action = null)
    {
        var toast = new ToastMessage(++lastId, message, kind, action);
        toasts.Add(toast);
        Changed?.Invoke();
        if (action?.Until is { } until)
        {
            _ = WithdrawActionAt(toast.Id, until);
        }
    }

    public void Dismiss(long id)
    {
        if (toasts.RemoveAll(toast => toast.Id == id) > 0)
        {
            Changed?.Invoke();
        }
    }

    #endregion

    #region Private Methods

    // Once an action has run out, its toast says only its message: a control that can no longer work does not stay.
    private async Task WithdrawActionAt(long id, DateTimeOffset until)
    {
        var left = until - time.GetUtcNow();
        if (left > TimeSpan.Zero)
        {
            await Task.Delay(left, time);
        }

        var index = toasts.FindIndex(toast => toast.Id == id);
        if (index >= 0)
        {
            toasts[index] = toasts[index] with { Action = null };
            Changed?.Invoke();
        }
    }

    #endregion
}
