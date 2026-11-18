namespace Drover.Web;

/// <summary>Reads a cookie of the page. Used for the antiforgery token, which the server leaves readable for this purpose.</summary>
public sealed class PageCookies(IJSRuntime js, ILogger<PageCookies> logger)
{
    #region Public Methods

    public async ValueTask<string?> Read(string name, CancellationToken cancellationToken)
    {
        try
        {
            var module = await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, "./cookies.js");
            await using (module)
            {
                return await module.InvokeAsync<string?>("read", cancellationToken, name);
            }
        }
        catch (JSException exception)
        {
            logger.LogError(exception, "The cookie script failed to read the antiforgery token");
            throw;
        }
    }

    #endregion
}
