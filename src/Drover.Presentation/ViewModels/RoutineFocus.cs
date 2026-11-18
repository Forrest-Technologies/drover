namespace Drover.Presentation.ViewModels;

/// <summary>Where the Routines section moves the focus after a change, in place of the control that went away.</summary>
public enum RoutineFocus
{
    /// <summary>The form of the routine being written.</summary>
    Form,

    /// <summary>The area with the button that adds a routine.</summary>
    Add,

    /// <summary>A row's link to the conversation a run started.</summary>
    Started,

    /// <summary>A row's "Run once now" button.</summary>
    RunOnce,
}
