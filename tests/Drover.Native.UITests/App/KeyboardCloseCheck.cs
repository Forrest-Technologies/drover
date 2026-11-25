namespace Drover.Native.Controls;

// Compiled into the app only for the screen checks. Gives the shared check the same close path a key press takes,
// without injecting a key the platform does not let one part of the app send to another.
public partial class Dialog
{
    internal void CloseFromKeyboardForCheck() => surface.CloseFromKeyboardForCheck();
}

internal sealed partial class DialogSurface
{
    internal void CloseFromKeyboardForCheck() => Close(FocusState.Keyboard);
}
