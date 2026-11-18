namespace Drover.Native.Controls;

/// <summary>One choice of a <see cref="Select"/>: the value kept and the words shown.</summary>
public sealed record SelectOption(string Value, string Label)
{
    public override string ToString() => Label;
}
