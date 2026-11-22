namespace Drover.Presentation.Diagnostics;

/// <summary>
/// Turns a log entry or a failure into the text kept in the diagnostic log. What is kept is a short list, not a
/// filter: the time, the level, the logger's name, the event number, the message template of Drover's own code, the
/// values of a few plain kinds (numbers, flags, durations, times, named choices), and for an exception its type, its
/// result code and the places in the code it passed through. Everything else is left out without being looked at:
/// formatted messages, text values, ids, an exception's message, the platform's and libraries' own messages, and the
/// folders the code was built in. So a token, a cookie, a session id, a server's address or password, a tool's
/// arguments or a message's text cannot reach the file, whatever form it arrives in.
/// </summary>
public static class DiagnosticEntry
{
    #region Private Fields

    private const string DroverCode = "Drover.";

    private const string TemplateName = "{OriginalFormat}";

    private const string LeftOut = "(message not kept)";

    private const int DeepestInner = 8;

    #endregion

    #region Public Methods

    public static string Line(
        DateTimeOffset at,
        LogLevel level,
        string category,
        EventId eventId,
        IReadOnlyList<KeyValuePair<string, object?>>? state,
        Exception? exception)
    {
        var line = new StringBuilder();
        line.Append(CultureInfo.InvariantCulture, $"{Stamp(at)} {Level(level)} {category}");
        if (eventId.Id != 0)
        {
            line.Append(CultureInfo.InvariantCulture, $" [{eventId.Id}]");
        }

        line.Append(": ");
        AppendMessage(line, category, state);
        line.Append('\n');

        if (exception is not null)
        {
            AppendException(line, exception, depth: 0);
        }

        return line.ToString();
    }

    /// <summary>A failure nothing handled: when it happened and what failed, in the terms of a log entry.</summary>
    public static string Failure(DateTimeOffset at, Exception exception)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{Stamp(at)} unhandled failure\n");
        AppendException(text, exception, depth: 0);
        return text.ToString();
    }

    #endregion

    #region Message

    // Drover's own templates are written in its code, never built from values (CODE_STYLE.md, Logging), so they carry
    // no data. Another library's message may be built from anything, so only its logger's name is kept.
    private static void AppendMessage(
        StringBuilder line,
        string category,
        IReadOnlyList<KeyValuePair<string, object?>>? state)
    {
        if (!category.StartsWith(DroverCode, StringComparison.Ordinal) || state is null)
        {
            line.Append(LeftOut);
            return;
        }

        var template = state.FirstOrDefault(pair => pair.Key == TemplateName).Value as string;
        line.Append(template ?? LeftOut);

        foreach (var (name, value) in state)
        {
            if (name != TemplateName && Plain(value) is { } kept)
            {
                line.Append(CultureInfo.InvariantCulture, $" {name}={kept}");
            }
        }
    }

    // A value is kept only when its kind cannot hold text. Strings, ids and anything else are left out.
    private static string? Plain(object? value) => value switch
    {
        Type type when type.GetType() == typeof(Exception).GetType() && typeof(Exception).IsAssignableFrom(type) => type.Name,
        bool flag => flag ? "true" : "false",
        Enum choice when Enum.IsDefined(choice.GetType(), choice) => choice.ToString(),
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
            Convert.ToString(value, CultureInfo.InvariantCulture),
        TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
        DateTimeOffset time => Stamp(time),
        DateTime time => time.ToString("O", CultureInfo.InvariantCulture),
        _ => null,
    };

    #endregion

    #region Exception

    private static void AppendException(StringBuilder text, Exception exception, int depth)
    {
        text.Append(
            CultureInfo.InvariantCulture,
            $"  {exception.GetType().FullName} (result 0x{exception.HResult:X8})\n");
        AppendFrames(text, exception);

        if (depth >= DeepestInner)
        {
            return;
        }

        IEnumerable<Exception> inner = exception switch
        {
            AggregateException many => many.InnerExceptions,
            { InnerException: { } one } => [one],
            _ => [],
        };

        foreach (var cause in inner)
        {
            text.Append("  caused by\n");
            AppendException(text, cause, depth + 1);
        }
    }

    // The frames are read from the runtime, not from the exception's text, which a type can override with anything.
    // A source file is named without its folders: those are the machine the app was built on.
    private static void AppendFrames(StringBuilder text, Exception exception)
    {
        foreach (var frame in new System.Diagnostics.StackTrace(exception, fNeedFileInfo: true).GetFrames())
        {
            var method = System.Diagnostics.DiagnosticMethodInfo.Create(frame);
            var place = method is null
                ? "(a method the runtime cannot name)"
                : $"{method.DeclaringTypeName}.{method.Name}";
            text.Append(CultureInfo.InvariantCulture, $"    at {place}");

            if (frame.GetFileName() is { Length: > 0 } file)
            {
                text.Append(CultureInfo.InvariantCulture, $" in {FileName(file)}:line {frame.GetFileLineNumber()}");
            }

            text.Append('\n');
        }
    }

    private static string FileName(string file) => file[(file.LastIndexOfAny(['/', '\\']) + 1)..];

    #endregion

    #region Formatting

    private static string Stamp(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string Level(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warning",
        LogLevel.Error => "error",
        LogLevel.Critical => "critical",
        _ => "none",
    };

    #endregion
}
