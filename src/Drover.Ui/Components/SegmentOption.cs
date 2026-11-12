namespace Drover.Ui.Components;

/// <summary>
/// One choice in a <see cref="SegmentedControl{TValue}"/>.
/// </summary>
public sealed record SegmentOption<TValue>(TValue Value, string Label);
