namespace Drover.Native.Pages;

/// <summary>
/// First-run setup, step two: the model the first bot answers with. It has not moved to this app yet: this class holds its place, and is
/// replaced by the screen when it moves.
/// </summary>
public sealed class ConnectModelPage() : PendingScreen("Connect a model", ScreenLayout.Entry);
