namespace Drover.Server.Tools;

/// <summary>
/// The page a browser shows after a tool server's sign-in page, when the sign-in was started in a native app. It
/// sends the browser into the app at once where the browser allows that, and offers the same address as a link
/// where it does not. The address is this server's own: the app's scheme and a one-time ticket.
/// </summary>
public static class AppReturnPage
{
    #region Public Methods

    public static ContentHttpResult For(string app)
    {
        var address = HtmlEncoder.Default.Encode(app);
        return TypedResults.Content(
            $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover" />
                <meta name="color-scheme" content="dark light" />
                <meta http-equiv="refresh" content="0;url={address}" />
                <title>Return to Drover</title>
                <link rel="icon" type="image/svg+xml" href="/icon.svg" />
                <link rel="stylesheet" href="/_content/Drover.Ui/tokens.css" />
                <link rel="stylesheet" href="/_content/Drover.Ui/base.css" />
            </head>
            <body>
                <main class="dv-boot">
                    <h1>Return to Drover</h1>
                    <p>Go back to the Drover app to finish the sign-in.</p>
                    <p><a href="{address}">Open Drover</a></p>
                </main>
            </body>
            </html>
            """,
            "text/html",
            Encoding.UTF8);
    }

    #endregion
}
