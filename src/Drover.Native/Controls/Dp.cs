namespace Drover.Native.Controls;

/// <summary>Registers a control's dependency properties with less ceremony.</summary>
internal static class Dp
{
    public static DependencyProperty Register<TOwner, TValue>(string name, TValue defaultValue, Action<TOwner>? changed = null)
        where TOwner : DependencyObject =>
        DependencyProperty.Register(
            name,
            typeof(TValue),
            typeof(TOwner),
            new PropertyMetadata(defaultValue, changed is null ? null : (owner, _) => changed((TOwner)owner)));
}
