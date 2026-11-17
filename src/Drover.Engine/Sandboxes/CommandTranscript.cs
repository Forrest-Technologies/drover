namespace Drover.Engine.Sandboxes;

/// <summary>
/// A running command's output as text: decoded per stream (a character split across two pieces is kept whole),
/// scrubbed of the sandbox's access token (a token split across two pieces is caught too, because the tail that could
/// start one is held back until more arrives), streamed to the run as it comes, and kept up to a limit for the result.
/// </summary>
public sealed class CommandTranscript(string token, int keepPerStream, Func<ToolOutputChunk, Task> output)
{
    #region Private Fields

    private readonly StreamText stdout = new(ToolOutputChunk.Stdout, token, keepPerStream);

    private readonly StreamText stderr = new(ToolOutputChunk.Stderr, token, keepPerStream);

    #endregion

    #region Properties

    public string Stdout => stdout.Kept;

    public string Stderr => stderr.Kept;

    /// <summary>Some output was left out of <see cref="Stdout"/> or <see cref="Stderr"/>.</summary>
    public bool Truncated => stdout.Truncated || stderr.Truncated;

    #endregion

    #region Public Methods

    /// <summary>Takes one piece of output. Returns the exit code when the piece says the command ended.</summary>
    public async Task<int?> Take(CommandOutput piece)
    {
        switch (piece)
        {
            case CommandStdout data:
                await Emit(stdout, stdout.Add(data.Data.Span));
                return null;
            case CommandStderr data:
                await Emit(stderr, stderr.Add(data.Data.Span));
                return null;
            case CommandExited exited:
                return exited.ExitCode;
            default:
                return null;
        }
    }

    /// <summary>Writes what was held back. Call once, after the last piece.</summary>
    public async Task Finish()
    {
        await Emit(stdout, stdout.Flush());
        await Emit(stderr, stderr.Flush());
    }

    /// <summary>The text with every whole occurrence of the token replaced.</summary>
    public static string Scrub(string text, string token) =>
        string.IsNullOrEmpty(token) ? text : text.Replace(token, Redactor.Mark, StringComparison.Ordinal);

    #endregion

    #region Private Methods

    private async Task Emit(StreamText stream, string text)
    {
        if (text.Length > 0)
        {
            await output(new ToolOutputChunk(stream.Name, text));
        }
    }

    #endregion

    #region Nested Types

    private sealed class StreamText(string name, string token, int keep)
    {
        #region Private Fields

        private readonly Decoder decoder = new UTF8Encoding(false).GetDecoder();

        private readonly StringBuilder kept = new();

        private string held = string.Empty;

        #endregion

        #region Properties

        public string Name => name;

        public string Kept => kept.ToString();

        public bool Truncated { get; private set; }

        #endregion

        #region Public Methods

        /// <summary>The text that is safe to show now. The end that could be the start of the token waits.</summary>
        public string Add(ReadOnlySpan<byte> bytes)
        {
            var chars = new char[decoder.GetCharCount(bytes, flush: false)];
            decoder.GetChars(bytes, chars, flush: false);
            var text = Scrub($"{held}{new string(chars)}", token);
            var wait = Math.Min(text.Length, Math.Max(token.Length - 1, 0));
            held = text[^wait..];
            return Keep(text[..^wait]);
        }

        public string Flush()
        {
            var rest = new char[decoder.GetCharCount([], flush: true)];
            decoder.GetChars([], rest, flush: true);
            var text = Scrub($"{held}{new string(rest)}", token);
            held = string.Empty;
            return Keep(text);
        }

        #endregion

        #region Private Methods

        private string Keep(string text)
        {
            var room = keep - kept.Length;

            if (text.Length > room)
            {
                Truncated = true;
            }

            kept.Append(text.AsSpan(0, Math.Min(text.Length, Math.Max(room, 0))));
            return text;
        }

        #endregion
    }

    #endregion
}
