namespace Drover.App;

public sealed class DroverPage : ContentPage
{
    #region Constructors

    public DroverPage(BlazorWebView view)
    {
        Content = view;
        SafeAreaEdges = SafeAreaEdges.Container;
    }

    #endregion

    #region Private Methods

    protected override bool OnBackButtonPressed()
    {
        if (Content?.Handler?.PlatformView is global::Android.Webkit.WebView browser && browser.CanGoBack())
        {
            browser.GoBack();
            return true;
        }

        return base.OnBackButtonPressed();
    }

    #endregion
}
