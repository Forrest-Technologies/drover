namespace Drover.Native.Pages;

/// <summary>
/// First-run setup, step one: the account that owns the server. It has not moved to this app yet: this class holds its place, and is
/// replaced by the screen when it moves.
/// </summary>
public sealed class CreateAccountPage() : PendingScreen("Create your account", ScreenLayout.Entry);
