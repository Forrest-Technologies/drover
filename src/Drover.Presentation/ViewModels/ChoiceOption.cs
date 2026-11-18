namespace Drover.Presentation.ViewModels;

/// <summary>
/// One choice of a list a screen offers: the value kept, the words shown, and the group it is listed under, when the
/// list has groups.
/// </summary>
public sealed record ChoiceOption(string Value, string Label, string? Group = null);
