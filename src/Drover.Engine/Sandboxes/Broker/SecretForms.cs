namespace Drover.Engine.Sandboxes.Broker;

/// <summary>
/// The shapes a secret value can take in text: as it is, base64 in three alignments (so a value inside a longer
/// encoded run is caught), base64url, hexadecimal in both cases, percent-encoded, JSON-escaped, and reversed. A long
/// value is also caught in part: any run of <see cref="PartLength"/> of its characters. Each form is a literal string,
/// built once.
/// </summary>
public static partial class SecretForms
{
    #region Properties

    /// <summary>A shorter value would also match ordinary words.</summary>
    public const int ShortestValue = 8;

    /// <summary>From this length a value is also hidden in part.</summary>
    public const int PartsFrom = 16;

    public const int PartLength = 12;

    #endregion

    #region Public Methods

    /// <summary>Every whole form of the value, longest first. None for a value shorter than <see cref="ShortestValue"/>.</summary>
    public static IReadOnlyList<string> Whole(string value)
    {
        if (value.Length < ShortestValue)
        {
            return [];
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        HashSet<string> forms = [value, Convert.ToHexString(bytes), Convert.ToHexString(bytes).ToLowerInvariant(), Reverse(value)];
        var escaped = Uri.EscapeDataString(value);
        forms.Add(escaped);
        forms.Add(LowercaseEscapes().Replace(escaped, match => match.Value.ToLowerInvariant()));
        var json = JsonSerializer.Serialize(value)[1..^1];
        forms.Add(json);
        forms.Add(json.Replace("/", "\\/", StringComparison.Ordinal));

        foreach (var form in Base64Forms(bytes))
        {
            forms.Add(form);
            forms.Add(form.Replace('+', '-').Replace('/', '_'));
        }

        return [.. forms.Where(form => form.Length >= ShortestValue).OrderByDescending(form => form.Length)];
    }

    /// <summary>The runs of <see cref="PartLength"/> characters of a value long enough to be hidden in part.</summary>
    public static IReadOnlyList<string> Parts(string value) =>
        value.Length < PartsFrom
            ? []
            : [.. Enumerable.Range(0, value.Length - PartLength + 1).Select(start => value.Substring(start, PartLength)).Distinct(StringComparer.Ordinal)];

    #endregion

    #region Private Methods

    /// <summary>
    /// The value's base64 when it starts a run, and when one or two bytes come before it: the characters that do not
    /// depend on the bytes around it, so a value encoded inside a longer text is still found.
    /// </summary>
    private static IEnumerable<string> Base64Forms(byte[] bytes)
    {
        for (var offset = 0; offset < 3; offset++)
        {
            var padded = new byte[offset + bytes.Length + 2];
            bytes.CopyTo(padded, offset);
            var encoded = Convert.ToBase64String(padded);

            // A character stands for six bits; keep those whose bits all come from the value.
            var first = (8 * offset + 5) / 6;
            var last = (8 * (offset + bytes.Length)) / 6 - 1;

            if (last >= first)
            {
                yield return encoded[first..(last + 1)];
            }
        }
    }

    [GeneratedRegex("%[0-9A-F]{2}", RegexOptions.CultureInvariant)]
    private static partial Regex LowercaseEscapes();

    private static string Reverse(string value)
    {
        var letters = value.ToCharArray();
        Array.Reverse(letters);
        return new string(letters);
    }

    #endregion
}
