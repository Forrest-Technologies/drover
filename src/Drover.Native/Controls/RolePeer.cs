namespace Drover.Native.Controls;

/// <summary>Tells assistive technology what a control that Drover draws itself is: a picture, a group, a tab.</summary>
internal sealed class RolePeer(FrameworkElement owner, AutomationControlType role) : FrameworkElementAutomationPeer(owner)
{
    #region Private Methods

    protected override AutomationControlType GetAutomationControlTypeCore() => role;

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    #endregion
}
