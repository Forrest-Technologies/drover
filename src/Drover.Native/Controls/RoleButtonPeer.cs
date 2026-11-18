namespace Drover.Native.Controls;

/// <summary>
/// A pressable control that is something more exact than a button to assistive technology: a link, a tab, a menu
/// item, one choice of several. It stays invokable, as a button is.
/// </summary>
internal sealed class RoleButtonPeer(Microsoft.UI.Xaml.Controls.Button owner, AutomationControlType role)
    : ButtonAutomationPeer(owner)
{
    #region Private Methods

    protected override AutomationControlType GetAutomationControlTypeCore() => role;

    #endregion
}
