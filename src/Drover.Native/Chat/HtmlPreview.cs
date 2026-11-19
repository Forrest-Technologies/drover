namespace Drover.Native.Chat;

/// <summary>A browser iframe with no sandbox capabilities. Native heads do not construct one.</summary>
internal sealed class HtmlPreview : Grid
{
    #region Private Fields

    private readonly ContentControl host = new();

    private readonly StatusNote loading = new() { Text = "Loading preview." };

    private Uno.UI.NativeElementHosting.BrowserHtmlElement? frame;

    private EventHandler<JSObject>? loaded;

    private string? shownAddress;

    #endregion

    #region Constructors

    public HtmlPreview()
    {
        Height = Tokens.Number("DvImageWidth");
        Children.Add(host);
        Children.Add(loading);
    }

    #endregion

    #region Public Methods

    public void Show(string address, string name)
    {
        if (address == shownAddress)
        {
            return;
        }

        Clear();
        if (!OperatingSystem.IsBrowser())
        {
            return;
        }

        frame = Uno.UI.NativeElementHosting.BrowserHtmlElement.CreateHtmlElement("iframe");
        loaded = (_, _) => loading.Text = null;
        frame.RegisterHtmlEventHandler("load", loaded);
        frame.SetHtmlAttribute(
            ("allow", string.Empty),
            ("credentialless", string.Empty),
            ("referrerpolicy", "no-referrer"),
            ("sandbox", string.Empty),
            ("src", address),
            ("title", $"Preview of {name}"));
        frame.SetCssStyle(("background", "transparent"), ("border", "0"), ("height", "100%"), ("width", "100%"));
        AutomationProperties.SetName(host, $"Preview of {name}");
        host.Content = frame;
        shownAddress = address;
        loading.Text = "Loading preview.";
    }

    public void Clear()
    {
        host.Content = null;
        if (frame is not null && loaded is not null)
        {
            frame.UnregisterHtmlEventHandler("load", loaded);
        }

        frame?.Dispose();
        frame = null;
        loaded = null;
        shownAddress = null;
        loading.Text = null;
    }

    #endregion
}
