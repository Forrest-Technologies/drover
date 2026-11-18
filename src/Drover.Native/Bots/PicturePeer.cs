namespace Drover.Native.Bots;

/// <summary>
/// What the sandbox's picture is to assistive technology: a button while the person drives the screen, which clicks
/// where the pointer is; a group that takes the focus while it is enlarged and only looked at; a picture otherwise.
/// </summary>
internal sealed class PicturePeer(SandboxPicture owner, SandboxScreenViewModel model)
    : FrameworkElementAutomationPeer(owner), Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider
{
    #region Interface Implementations

    public async void Invoke()
    {
        if (model.Control)
        {
            await model.ClickAtPointer();
        }
    }

    #endregion

    #region Private Methods

    protected override AutomationControlType GetAutomationControlTypeCore() =>
        model.Control ? AutomationControlType.Button
        : model.Zoom.IsFit ? AutomationControlType.Image
        : AutomationControlType.Group;

    protected override object? GetPatternCore(PatternInterface patternInterface) =>
        patternInterface is PatternInterface.Invoke && model.Control ? this : base.GetPatternCore(patternInterface);

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    #endregion
}
