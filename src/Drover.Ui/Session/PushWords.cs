namespace Drover.Ui.Session;

/// <summary>Whether this device gets notifications, as the browser and the server report it.</summary>
public enum PushState
{
    Off,
    On,
    Blocked,
    NotSupported,
    NeedsHomeScreen,
    ServerNotReady,
}

/// <summary>Notifications on one device, in words. Every state says what is so and what to do about it.</summary>
public static class PushWords
{
    #region Properties

    /// <summary>What a notification will and will not show, said wherever a person can turn them on or off.</summary>
    public static string Contents => "A notification names the bot and what happened. It never shows what was said.";

    #endregion

    #region Public Methods

    public static string Sentence(PushState state) => state switch
    {
        PushState.On => "This device is told when a bot finishes or needs you.",
        PushState.Off => "This device is not told when a bot finishes or needs you.",
        PushState.Blocked => "This browser blocks notifications from Drover. Allow them in the browser's settings for this site, then turn them on here.",
        PushState.NeedsHomeScreen => "On this device, notifications work once Drover is added to the Home Screen. Add it from the browser's share menu, then open it from there.",
        PushState.ServerNotReady => "This server cannot send notifications yet. Its operator needs to set the public https address people use to open Drover.",
        _ => "This browser has no support for web notifications. Open Drover in a browser that does.",
    };

    /// <summary>The one action for a state, or null when the next step is outside Drover.</summary>
    public static string? Action(PushState state) => state switch
    {
        PushState.Off => "Turn on notifications on this device",
        PushState.On => "Turn off notifications on this device",
        _ => null,
    };

    #endregion
}
