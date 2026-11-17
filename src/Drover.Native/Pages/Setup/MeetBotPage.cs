namespace Drover.Native.Pages;

/// <summary>
/// First-run setup, step three: the first bot. It has not moved to this app yet: this class holds its place, and is
/// replaced by the screen when it moves.
/// </summary>
public sealed class MeetBotPage() : PendingScreen("Meet your first bot", ScreenLayout.Entry);
