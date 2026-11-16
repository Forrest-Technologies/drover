namespace Drover.Ui.Components;

/// <summary>
/// One toast on screen. The id is what a dismissal names. <see cref="Action"/> is the one thing the toast offers
/// besides dismissing it, such as Undo.
/// </summary>
public sealed record ToastMessage(long Id, string Message, ToastKind Kind, ToastAction? Action = null)
{
    internal bool Held { get; init; }
}


/// <summary>
/// What a toast's one action is called and what it does. Choosing it also dismisses the toast. <see cref="Name"/> is
/// its accessible name when the label alone does not say what it acts on. Past <see cref="Until"/> the action can no
/// longer work, so the toast stops offering it and keeps only its message.
/// </summary>
public sealed record ToastAction(string Label, Func<Task> Run, string? Name = null, DateTimeOffset? Until = null);
