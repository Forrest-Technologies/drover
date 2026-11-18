namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// What a part of the workspace says when it has nothing to show: why, in plain words, and the one thing to do
/// next. Without an address the action is the screen's own, such as trying again.
/// </summary>
public sealed record EmptyNotice(string Title, string Cause, string ActionLabel, string? ActionHref);
