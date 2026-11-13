namespace Drover.Ui.Components;

/// <summary>
/// One choice in a <see cref="SegmentedControl{TValue}"/>. A disabled choice is shown and cannot be taken.
/// </summary>
public sealed record SegmentOption<TValue>(TValue Value, string Label, bool Disabled = false);
