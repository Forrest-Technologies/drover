namespace Drover.Native.Pages;

/// <summary>The browser leaves the page for the provider's, in the same tab, as a link would.</summary>
internal static partial class ProviderPage
{
    #region Private Methods

    private static partial bool TryLeaveThePage(string address)
    {
        Assign(address);
        return true;
    }

    [JSImport("globalThis.location.assign")]
    private static partial void Assign(string address);

    #endregion
}
