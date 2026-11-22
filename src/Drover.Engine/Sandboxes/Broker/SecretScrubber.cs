namespace Drover.Engine.Sandboxes.Broker;

/// <summary>
/// Replaces secret values in text, in every form <see cref="SecretForms"/> knows, with a mark. For text that arrives
/// in pieces, <see cref="Stream"/> holds back the end of each piece that could start a form until more arrives.
/// </summary>
public sealed class SecretScrubber
{
    #region Private Fields

    private readonly string mark;

    private readonly string[] whole;

    private readonly HashSet<string> parts;

    #endregion

    #region Constructors

    /// <param name="values">The secret values.</param>
    /// <param name="mark">What each form is replaced with.</param>
    /// <param name="bytes">
    /// Whether the text to scrub is bytes read one character each (Latin-1), as a response body of any type is: each form
    /// is then spelled as its UTF-8 bytes read that way, so binary bodies pass through unchanged but for the forms.
    /// </param>
    public SecretScrubber(IEnumerable<string> values, string mark, bool bytes = false)
    {
        this.mark = mark;
        var known = values.Where(value => !string.IsNullOrEmpty(value)).Distinct(StringComparer.Ordinal).ToList();
        Func<string, string> spell = bytes ? form => Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(form)) : form => form;
        whole = [.. known.SelectMany(SecretForms.Whole).Select(spell).Distinct(StringComparer.Ordinal).OrderByDescending(form => form.Length)];
        parts = [.. known.SelectMany(SecretForms.Parts).Select(spell)];
        Longest = whole.Length == 0 ? 0 : whole[0].Length;
    }

    #endregion

    #region Properties

    public bool IsEmpty => whole.Length == 0;

    /// <summary>The longest form, which is how much a stream must hold back.</summary>
    public int Longest { get; }

    #endregion

    #region Public Methods

    public string Scrub(string text)
    {
        if (IsEmpty || text.Length == 0)
        {
            return text;
        }

        foreach (var form in whole)
        {
            text = text.Replace(form, mark, StringComparison.Ordinal);
        }

        return parts.Count == 0 ? text : ScrubParts(text);
    }

    /// <summary>A scrubber for text that arrives in pieces.</summary>
    public StreamScrub Stream() => new(this);

    #endregion

    #region Private Methods

    /// <summary>Marks every run of characters that is a part of a value, joining runs that overlap.</summary>
    private string ScrubParts(string text)
    {
        if (text.Length < SecretForms.PartLength)
        {
            return text;
        }

        var hidden = new bool[text.Length];
        var any = false;

        for (var start = 0; start + SecretForms.PartLength <= text.Length; start++)
        {
            if (parts.Contains(text.Substring(start, SecretForms.PartLength)))
            {
                Array.Fill(hidden, true, start, SecretForms.PartLength);
                any = true;
            }
        }

        if (!any)
        {
            return text;
        }

        var built = new StringBuilder(text.Length);

        for (var index = 0; index < text.Length; index++)
        {
            if (!hidden[index])
            {
                built.Append(text[index]);
            }
            else if (index == 0 || !hidden[index - 1])
            {
                built.Append(mark);
            }
        }

        return built.ToString();
    }

    #endregion

    #region Nested Types

    /// <summary>Scrubs text that arrives in pieces: a form split across two pieces is caught.</summary>
    public sealed class StreamScrub(SecretScrubber scrubber)
    {
        #region Private Fields

        private string held = string.Empty;

        #endregion

        #region Public Methods

        /// <summary>The text that is safe to pass on now. The end that could start a form waits for the next piece.</summary>
        public string Add(string piece)
        {
            if (scrubber.IsEmpty)
            {
                return piece;
            }

            var scrubbed = scrubber.Scrub($"{held}{piece}");
            var wait = Math.Min(scrubbed.Length, scrubber.Longest - 1);
            held = scrubbed[^wait..];
            return scrubbed[..^wait];
        }

        public string Flush()
        {
            var text = scrubber.Scrub(held);
            held = string.Empty;
            return text;
        }

        #endregion
    }

    #endregion
}
