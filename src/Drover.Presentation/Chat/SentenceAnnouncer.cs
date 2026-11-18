namespace Drover.Ui.Chat;

/// <summary>
/// Decides what a screen reader hears while a reply streams: each sentence once, when it is complete,
/// instead of every fragment as it arrives.
/// </summary>
public sealed class SentenceAnnouncer
{
    #region Private Fields

    private static readonly char[] sentenceEnds = ['.', '?', '!', '\n'];

    private string? messageKey;

    private int announced;

    #endregion

    #region Public Methods

    /// <summary>
    /// The newly completed sentences of the message, or nothing when no sentence has finished since the last call.
    /// A message that has stopped streaming gives up whatever remains.
    /// </summary>
    public string? Next(string key, string text, bool streaming)
    {
        if (key != messageKey)
        {
            messageKey = key;
            announced = 0;
        }

        var end = streaming ? LastSentenceEnd(text) : text.Length;
        if (end <= announced)
        {
            return null;
        }

        var sentences = text[announced..end].Trim();
        announced = end;
        return sentences.Length == 0 ? null : sentences;
    }

    #endregion

    #region Private Methods

    // A sentence has ended once its mark is followed by white space. A mark at the very end may still be
    // the middle of "3.14" or of an ellipsis, so it waits for the next fragment.
    private int LastSentenceEnd(string text)
    {
        for (var index = text.Length - 2; index >= announced; index--)
        {
            if (sentenceEnds.Contains(text[index]) && char.IsWhiteSpace(text[index + 1]))
            {
                return index + 1;
            }

            if (text[index] is '\n')
            {
                return index + 1;
            }
        }

        return announced;
    }

    #endregion
}
