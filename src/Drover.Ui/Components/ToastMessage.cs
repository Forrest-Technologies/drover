namespace Drover.Ui.Components;

/// <summary>
/// One toast on screen. The id is what a dismissal names.
/// </summary>
public sealed record ToastMessage(long Id, string Message, ToastKind Kind);
