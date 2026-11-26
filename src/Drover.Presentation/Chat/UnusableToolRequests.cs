namespace Drover.Ui.Chat;

/// <summary>
/// Recognizes a completed reply that contains only a raw tool-call protocol envelope instead of ordinary prose or a
/// structured tool call, and holds the plain-word explanation shown in its place. Raw text is never parsed into an
/// executable tool call; the original text remains untouched on the message for copy, export, and activity.
/// </summary>
public static class UnusableToolRequests
{
    #region Private Fields

    private const char FullWidthBar = '\uFF5C';

    private const char AsciiBar = '|';

    private const string FullWidthPrefix = "<\uFF5CDSML\uFF5C";

    private const string AsciiPrefix = "<|DSML|";

    private const string FullWidthClosePrefix = "</\uFF5CDSML\uFF5C";

    private const string AsciiClosePrefix = "</|DSML|";

    private const string FullWidthParameterClose = "</\uFF5CDSML\uFF5Cparameter";

    private const string AsciiParameterClose = "</|DSML|parameter";

    private const string CallsTag = "calls";

    private const string FunctionCallsTag = "function_calls";

    private const string InvokeTag = "invoke";

    private const string ParameterTag = "parameter";

    #endregion

    #region Public Methods

    /// <summary>
    /// True only when the entire trimmed text consists of one or more complete DSML tool-call envelopes containing at
    /// least one invocation marker, with no surrounding prose, code fences, or quoting.
    /// </summary>
    public static bool IsProtocolEnvelope(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().Trim();
        var index = 0;
        var envelopes = 0;

        while (index < span.Length)
        {
            SkipWhitespace(span, ref index);

            if (!TryReadEnvelope(span, ref index))
            {
                return false;
            }

            envelopes++;
            SkipWhitespace(span, ref index);
        }

        return envelopes > 0;
    }

    /// <summary>
    /// True when a completed reply starts with a DSML envelope, holds an invocation marker, and ends before that
    /// envelope's closing tag. Text that closed the envelope, quoted examples, code fences, and prose are not marked.
    /// </summary>
    public static bool IsCutOffProtocolRequest(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().Trim();
        if (span.Length < 2 || span[0] != '<' || span[1] is not (FullWidthBar or AsciiBar))
        {
            return false;
        }

        var bar = span[1];
        var index = 0;
        if (!TryMatchWrapperOpen(span, ref index, bar, out var wrapper) || !HasInvocationMarker(span, index, bar))
        {
            return false;
        }

        return !HasWrapperClose(span, bar, wrapper, index);
    }

    /// <summary>True for a completed reply that holds only a raw tool request, whole or cut off before its closing tags.</summary>
    public static bool IsUnusableRequest(string? text) =>
        IsProtocolEnvelope(text) || IsCutOffProtocolRequest(text);

    /// <summary>The heading explaining that the model returned a tool request Drover could not use.</summary>
    public static string Title(string botName) =>
        string.IsNullOrWhiteSpace(botName)
            ? "The model returned a tool request that could not be used."
            : $"{botName}'s model returned a tool request that could not be used.";

    /// <summary>The supporting sentence confirming no tool ran and naming the next step when retry is available.</summary>
    public static string Detail(bool canRetry) =>
        canRetry ? "No tool ran. Try again to ask for another reply." : "No tool ran.";

    /// <summary>The accessible name of a conversation entry that holds an unusable tool-request reply.</summary>
    public static string EntryName(string botName) =>
        string.IsNullOrWhiteSpace(botName)
            ? "Unusable tool request"
            : $"{botName} returned an unusable tool request";

    /// <summary>The plain-word announcement read by assistive technology when an unusable tool-request reply appears.</summary>
    public static string Announcement(string botName, bool canRetry) =>
        $"{Title(botName)} {Detail(canRetry)}";

    #endregion

    #region Private Methods

    private static bool TryReadEnvelope(ReadOnlySpan<char> span, ref int index)
    {
        if (index + 1 >= span.Length || span[index] != '<')
        {
            return false;
        }

        var bar = span[index + 1];

        if (bar is not (FullWidthBar or AsciiBar))
        {
            return false;
        }

        if (!TryMatchWrapperOpen(span, ref index, bar, out var wrapper))
        {
            return false;
        }

        var invocations = 0;

        while (index < span.Length)
        {
            SkipWhitespace(span, ref index);

            if (TryMatchCloseTag(span, ref index, bar, wrapper))
            {
                return invocations > 0;
            }

            if (!TryReadInvoke(span, ref index, bar))
            {
                return false;
            }

            invocations++;
        }

        return false;
    }

    private static bool TryMatchWrapperOpen(ReadOnlySpan<char> span, ref int index, char bar, out string wrapper)
    {
        var start = index;
        var prefix = OpenPrefix(bar);

        if (!span[index..].StartsWith(prefix, StringComparison.Ordinal))
        {
            wrapper = string.Empty;
            return false;
        }

        index += prefix.Length;

        if (span[index..].StartsWith(FunctionCallsTag, StringComparison.Ordinal))
        {
            wrapper = FunctionCallsTag;
            index += FunctionCallsTag.Length;
        }
        else if (span[index..].StartsWith(CallsTag, StringComparison.Ordinal))
        {
            wrapper = CallsTag;
            index += CallsTag.Length;
        }
        else
        {
            index = start;
            wrapper = string.Empty;
            return false;
        }

        SkipWhitespace(span, ref index);

        if (index >= span.Length || span[index] != '>')
        {
            index = start;
            wrapper = string.Empty;
            return false;
        }

        index++;
        return true;
    }

    private static bool TryReadInvoke(ReadOnlySpan<char> span, ref int index, char bar)
    {
        if (!TryMatchNamedOpenTag(span, ref index, bar, InvokeTag))
        {
            return false;
        }

        if (!TryReadOpenTagEnd(span, ref index, out var selfClosing))
        {
            return false;
        }

        if (selfClosing)
        {
            return true;
        }

        while (index < span.Length)
        {
            SkipWhitespace(span, ref index);

            if (TryMatchCloseTag(span, ref index, bar, InvokeTag))
            {
                return true;
            }

            if (!TryReadParameter(span, ref index, bar))
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryReadParameter(ReadOnlySpan<char> span, ref int index, char bar)
    {
        if (!TryMatchNamedOpenTag(span, ref index, bar, ParameterTag))
        {
            return false;
        }

        if (!TryReadOpenTagEnd(span, ref index, out var selfClosing))
        {
            return false;
        }

        if (selfClosing)
        {
            return true;
        }

        var closePrefix = bar == FullWidthBar ? FullWidthParameterClose : AsciiParameterClose;
        var search = index;

        while (search < span.Length)
        {
            var offset = span[search..].IndexOf(closePrefix, StringComparison.Ordinal);

            if (offset < 0)
            {
                return false;
            }

            var after = search + offset + closePrefix.Length;
            SkipWhitespace(span, ref after);

            if (after < span.Length && span[after] == '>')
            {
                index = after + 1;
                return true;
            }

            search += offset + closePrefix.Length;
        }

        return false;
    }

    private static bool TryMatchNamedOpenTag(ReadOnlySpan<char> span, ref int index, char bar, string name)
    {
        var start = index;
        var prefix = OpenPrefix(bar);

        if (!span[index..].StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        index += prefix.Length;

        if (!span[index..].StartsWith(name, StringComparison.Ordinal))
        {
            index = start;
            return false;
        }

        index += name.Length;

        if (index >= span.Length || !(char.IsWhiteSpace(span[index]) || span[index] is '/' or '>'))
        {
            index = start;
            return false;
        }

        return true;
    }

    private static bool TryReadOpenTagEnd(ReadOnlySpan<char> span, ref int index, out bool selfClosing)
    {
        var quote = '\0';

        for (var position = index; position < span.Length; position++)
        {
            var current = span[position];

            if (quote != '\0')
            {
                if (current == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (current is '"' or '\'')
            {
                quote = current;
                continue;
            }

            if (current == '<')
            {
                selfClosing = false;
                return false;
            }

            if (current == '>')
            {
                selfClosing = position > index && span[position - 1] == '/';
                index = position + 1;
                return true;
            }
        }

        selfClosing = false;
        return false;
    }

    private static bool TryMatchCloseTag(ReadOnlySpan<char> span, ref int index, char bar, string name)
    {
        var start = index;
        var prefix = bar == FullWidthBar ? FullWidthClosePrefix : AsciiClosePrefix;

        if (!span[index..].StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        index += prefix.Length;

        if (!span[index..].StartsWith(name, StringComparison.Ordinal))
        {
            index = start;
            return false;
        }

        index += name.Length;
        SkipWhitespace(span, ref index);

        if (index >= span.Length || span[index] != '>')
        {
            index = start;
            return false;
        }

        index++;
        return true;
    }

    private static bool HasInvocationMarker(ReadOnlySpan<char> span, int from, char bar)
    {
        var marker = $"{OpenPrefix(bar)}{InvokeTag}";
        var search = from;

        while (search < span.Length)
        {
            var offset = span[search..].IndexOf(marker, StringComparison.Ordinal);
            if (offset < 0)
            {
                return false;
            }

            var after = search + offset + marker.Length;
            if (after < span.Length && (char.IsWhiteSpace(span[after]) || span[after] is '/' or '>'))
            {
                return true;
            }

            search = after;
        }

        return false;
    }

    private static bool HasWrapperClose(ReadOnlySpan<char> span, char bar, string wrapper, int from)
    {
        var prefix = bar == FullWidthBar ? FullWidthClosePrefix : AsciiClosePrefix;
        var close = $"{prefix}{wrapper}";
        return span[from..].IndexOf(close, StringComparison.Ordinal) >= 0;
    }

    private static string OpenPrefix(char bar) =>
        bar == FullWidthBar ? FullWidthPrefix : AsciiPrefix;

    private static void SkipWhitespace(ReadOnlySpan<char> span, ref int index)
    {
        while (index < span.Length && char.IsWhiteSpace(span[index]))
        {
            index++;
        }
    }

    #endregion

    #region Fixed Words

    public const string RetryLabel = "Try again";

    public const string OriginalResponseTitle = "Original response";

    #endregion
}
