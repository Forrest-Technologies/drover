namespace Drover.Presentation.ViewModels;

/// <summary>
/// The toasts on screen, in two lists as the host reads them out: the quiet ones, and the errors that interrupt.
/// </summary>
public sealed partial class ToastHostViewModel : ObservableObject, IDisposable
{
    #region Private Fields

    private readonly ToastService toasts;

    #endregion

    #region Constructors

    // A traditional constructor, because the host listens to the toast service from the moment it exists.
#pragma warning disable IDE0290
    public ToastHostViewModel(ToastService toasts)
#pragma warning restore IDE0290
    {
        this.toasts = toasts;
        toasts.Changed += Refresh;
    }

    #endregion

    #region Properties

    public IReadOnlyList<ToastMessage> Quiet => [.. toasts.Toasts.Where(toast => toast.Kind is not ToastKind.Error)];

    public IReadOnlyList<ToastMessage> Errors => [.. toasts.Toasts.Where(toast => toast.Kind is ToastKind.Error)];

    /// <summary>Raised when the lists changed. It can come from any thread; the host moves to its own.</summary>
    public event Action? Changed;

    #endregion

    #region Public Methods

    public void Dispose() => toasts.Changed -= Refresh;

    /// <summary>The word that carries the kind. The color only repeats it.</summary>
    public static string? KindWord(ToastKind kind) => kind switch
    {
        ToastKind.Warning => "Warning",
        ToastKind.Error => "Error",
        _ => null,
    };

    public void Dismiss(ToastMessage toast) => toasts.Dismiss(toast.Id);

    /// <summary>A toast a person is reading or reaching for stays until they dismiss it.</summary>
    public void Hold(ToastMessage toast) => toasts.Hold(toast.Id);

    /// <summary>Dismissed first, so the action cannot be chosen twice while it runs.</summary>
    public Task Act(ToastMessage toast)
    {
        if (toast.Action is not { } action)
        {
            return Task.CompletedTask;
        }

        toasts.Dismiss(toast.Id);
        return action.Run();
    }

    #endregion

    #region Private Methods

    private void Refresh()
    {
        OnPropertyChanged(nameof(Quiet));
        OnPropertyChanged(nameof(Errors));
        Changed?.Invoke();
    }

    #endregion
}
