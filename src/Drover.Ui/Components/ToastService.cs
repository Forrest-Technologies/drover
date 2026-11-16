namespace Drover.Ui.Components;

/// <summary>
/// Holds the toasts on screen. Routine confirmations clear after a short delay. Warnings, errors and actions stay
/// available for the person reading them.
/// </summary>
public sealed class ToastService(TimeProvider time)
{
    #region Private Fields

    private static readonly TimeSpan toastLifetime = TimeSpan.FromSeconds(6);

    private ImmutableList<ToastMessage> toasts = [];

    private long lastId;

    #endregion

    #region Properties

    public IReadOnlyList<ToastMessage> Toasts => toasts;

    public event Action? Changed;

    #endregion

    #region Public Methods

    public void Show(string message, ToastKind kind = ToastKind.Info, ToastAction? action = null)
    {
        var toast = new ToastMessage(Interlocked.Increment(ref lastId), message, kind, action);
        ImmutableInterlocked.Update(ref toasts, items => items.Add(toast));

        Changed?.Invoke();
        _ = Expire(toast);
    }

    public void Dismiss(long id) => Remove(id, automatic: false);

    /// <summary>Keep a toast on screen once someone focuses one of its controls.</summary>
    public void Hold(long id) => UpdateToast(id, toast => toast with { Held = true });

    #endregion

    #region Private Methods

    private async Task Expire(ToastMessage toast)
    {
        if (toast.Action is { } action)
        {
            if (action.Until is not { } until)
            {
                return;
            }

            var left = until - time.GetUtcNow();
            if (left > TimeSpan.Zero)
            {
                await Task.Delay(left, time);
            }

            if (!WithdrawAction(toast.Id))
            {
                return;
            }
        }

        if (toast.Kind is ToastKind.Info or ToastKind.Success)
        {
            await Task.Delay(toastLifetime, time);
            Remove(toast.Id, automatic: true);
        }
    }

    private bool WithdrawAction(long id)
    {
        if (!UpdateToast(id, toast => toast with { Action = null }))
        {
            return false;
        }

        Changed?.Invoke();
        return true;
    }

    private bool UpdateToast(long id, Func<ToastMessage, ToastMessage> update) =>
        ImmutableInterlocked.Update(ref toasts, items =>
        {
            var index = items.FindIndex(toast => toast.Id == id);
            return index < 0 ? items : items.SetItem(index, update(items[index]));
        });

    private void Remove(long id, bool automatic)
    {
        if (ImmutableInterlocked.Update(ref toasts, items =>
                items.RemoveAll(toast => toast.Id == id && (!automatic || !toast.Held))))
        {
            Changed?.Invoke();
        }
    }

    #endregion
}
