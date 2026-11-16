namespace Drover.Ui.Components;

/// <summary>
/// What a toast reports. Warnings and errors are named in words. Errors interrupt the screen reader.
/// </summary>
public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}
