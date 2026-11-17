namespace Drover.Presentation.Routing;

/// <summary>Takes an in-app address apart: "/tools?sign-in=done#top" is a path, a query, and a fragment.</summary>
public static class AddressParts
{
    #region Public Methods

    public static (string Path, string Query, string? Fragment) Split(string address)
    {
        var hash = address.IndexOf('#', StringComparison.Ordinal);
        var fragment = hash < 0 ? null : address[(hash + 1)..];
        var rest = hash < 0 ? address : address[..hash];
        var mark = rest.IndexOf('?', StringComparison.Ordinal);

        return mark < 0 ? (rest, string.Empty, fragment) : (rest[..mark], rest[(mark + 1)..], fragment);
    }

    /// <summary>The path alone, without a trailing slash, for telling whether two addresses name the same place.</summary>
    public static string Path(string address) => Split(address).Path.TrimEnd('/') is { Length: > 0 } path ? path : "/";

    public static IReadOnlyDictionary<string, string> ReadQuery(string query)
    {
        Dictionary<string, string> values = [with(StringComparer.OrdinalIgnoreCase)];

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            values[Unescape(parts[0])] = parts.Length == 2 ? Unescape(parts[1]) : string.Empty;
        }

        return values;
    }

    #endregion

    #region Private Methods

    private static string Unescape(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));

    #endregion
}
