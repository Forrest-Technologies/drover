namespace Drover.Ui.Components;

/// <summary>
/// What a toast reports. The kind is always shown as a word, and an error interrupts the screen reader.
/// </summary>
public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}
