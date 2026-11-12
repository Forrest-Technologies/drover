namespace Drover.Engine.Runs;

/// <summary>
/// Replaces secrets in tool arguments and results before they are stored or emitted: exact matches of every secret
/// the run has revealed, and common token shapes. It is a seatbelt, not a guarantee: a secret returned in another
/// form (encoded, split) is not caught. The primary control is that secrets do not enter tool arguments at all.
/// </summary>
public sealed partial class Redactor(IEnumerable<string?> revealedSecrets)
{
    #region Private Fields

    public const string Mark = "[redacted]";

    /// <summary>A shorter value would also match ordinary words, and hide half of every result.</summary>
    private const int ShortestSecret = 8;

    private readonly List<string> secrets =
    [
        .. revealedSecrets
            .OfType<string>()
            .Where(secret => secret.Length >= ShortestSecret)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(secret => secret.Length),
    ];

    #endregion

    #region Public Methods

    public string Redact(string text)
    {
        foreach (var secret in secrets)
        {
            text = text.Replace(secret, Mark, StringComparison.Ordinal);
        }

        return TokenShapes().Replace(text, Mark);
    }

    /// <summary>The same JSON with every string in it, keys included, redacted.</summary>
    public JsonElement Redact(JsonElement value) => JsonSerializer.SerializeToElement(RedactNode(value));

    #endregion

    #region Private Methods

    private JsonNode? RedactNode(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => RedactObject(value),
        JsonValueKind.Array => new JsonArray([.. value.EnumerateArray().Select(RedactNode)]),
        JsonValueKind.String => JsonValue.Create(Redact(value.GetString() ?? string.Empty)),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => JsonNode.Parse(value.GetRawText()),
    };

    /// <summary>
    /// Keys are assigned, not added. Model-written arguments can repeat a key, and two keys can redact to the same
    /// text; neither may fail the run, so the last one wins, as it does for most readers of JSON.
    /// </summary>
    private JsonObject RedactObject(JsonElement value)
    {
        var redacted = new JsonObject();

        foreach (var property in value.EnumerateObject())
        {
            redacted[Redact(property.Name)] = RedactNode(property.Value);
        }

        return redacted;
    }

    // Provider keys, GitHub and Slack tokens, AWS access key ids, JSON web tokens, bearer headers, PEM private keys.
    [GeneratedRegex(
        """
        sk-[A-Za-z0-9_\-]{20,}
        |gh[pousr]_[A-Za-z0-9]{30,}
        |github_pat_[A-Za-z0-9_]{22,}
        |xox[abprs]-[A-Za-z0-9\-]{10,}
        |AKIA[0-9A-Z]{16}
        |eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}
        |(?<=[Bb]earer\ )[A-Za-z0-9._~+/\-]{20,}=*
        |-----BEGIN\ [A-Z\ ]*PRIVATE\ KEY-----[\s\S]*?-----END\ [A-Z\ ]*PRIVATE\ KEY-----
        """,
        RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex TokenShapes();

    #endregion
}
