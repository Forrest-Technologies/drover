namespace Drover.Ui.Chat;

/// <summary>
/// A source a reply cites, as the conversation shows it: its title, the address it opens outside the app, and the
/// site, said beside the title so a misleading title cannot hide where the link goes. The title and the address came
/// from the model's provider: they are shown as plain text and never followed inside the app.
/// </summary>
public sealed record SourceLink(string Title, string Address, string Site)
{
    #region Private Fields

    private const int LongestTitle = 160;

    #endregion

    #region Public Methods

    /// <summary>The source an address and title make, or null when the address is not a web address.</summary>
    public static SourceLink? From(string? address, string? title)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var site = url.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? url.Host[4..] : url.Host;
        var named = string.IsNullOrWhiteSpace(title) ? site : string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return new(named.Length > LongestTitle ? $"{named[..(LongestTitle - 1)]}…" : named, url.AbsoluteUri, site);
    }

    #endregion
}
