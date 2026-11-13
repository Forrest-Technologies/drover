namespace Drover.Ui;

/// <summary>
/// What the host tells the interface about where it is running. The web host and the native shell each register one.
/// </summary>
public sealed record UiEnvironment(bool IsDevelopment);
