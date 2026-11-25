namespace Drover.Native.Controls;

// Compiled into the app only for the screen checks. The platform gives one part of the app no public way to send a
// key to another, so the check takes the same keyboard close path a pressed Escape takes: FocusState.Keyboard, which
// returns the focus to the control that opened the surface. Escape's own KeyDown handler calls this same Close.
public partial class Dialog
{
    internal void CloseFromKeyboardForCheck() => surface.CloseFromKeyboardForCheck();
}

internal sealed partial class DialogSurface
{
    internal void CloseFromKeyboardForCheck() => Close(FocusState.Keyboard);
}
