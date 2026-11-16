namespace Drover.Ui.Components;

/// <summary>
/// One toast on screen. The id is what a dismissal names. <see cref="Action"/> is the one thing the toast offers
/// besides dismissing it, such as Undo.
/// </summary>
public sealed record ToastMessage(long Id, string Message, ToastKind Kind, ToastAction? Action = null);


/// <summary>What a toast's one action is called and what it does. Choosing it also dismisses the toast.</summary>
public sealed record ToastAction(string Label, Func<Task> Run);
