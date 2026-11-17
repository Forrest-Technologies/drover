namespace Drover.Native.Pages;

/// <summary>
/// A bot's sandbox: its screen, its terminal, and its power controls. It has not moved to this app yet: this class holds its place, and is
/// replaced by the screen when it moves.
/// </summary>
public sealed class BotSandboxPage() : PendingScreen("Sandbox", ScreenLayout.Work);
