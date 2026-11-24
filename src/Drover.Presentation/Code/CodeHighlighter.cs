namespace Drover.Presentation.Code;

/// <summary>
/// The parts of a block of code that are drawn apart from plain text: keywords, strings, comments, numbers and literal
/// words, keys, and punctuation. What lies between the parts is plain text. A small scanner per family reads it, never
/// a full parser, so it costs one pass over text no longer than <see cref="CodeLines.MaxCharacters"/>; a longer text
/// is drawn plain.
/// </summary>
public static class CodeHighlighter
{
    #region Private Fields

    private const string PunctuationCharacters = "{}[]();,.:=<>+-*/%!&|^~?";

    #endregion

    #region Public Methods

    /// <summary>The parts of the text to draw apart, in order and never overlapping.</summary>
    public static IReadOnlyList<CodeToken> Tokens(string text, CodeFamily family)
    {
        if (text.Length > CodeLines.MaxCharacters)
        {
            return [];
        }

        if (family is CodeFamily.Markup)
        {
            return MarkupTokens(text);
        }

        return CodeGrammar.For(family) is { } grammar ? new Scanner(text, grammar).Read() : [];
    }

    #endregion

    #region Markup

    // A tag's name is a keyword, its attributes' names are keys and their values strings, and its brackets
    // punctuation. A comment is a comment. The text between tags is plain.
    private static List<CodeToken> MarkupTokens(string text)
    {
        List<CodeToken> tokens = [];
        var index = 0;

        while (index < text.Length)
        {
            if (text.AsSpan(index).StartsWith("<!--", StringComparison.Ordinal))
            {
                var close = text.IndexOf("-->", index + 4, StringComparison.Ordinal);
                var end = close < 0 ? text.Length : close + 3;
                tokens.Add(new(index, end - index, CodeTokenKind.Comment));
                index = end;
                continue;
            }

            if (text[index] is '<' && index + 1 < text.Length && (char.IsLetter(text[index + 1]) || text[index + 1] is '/' or '!' or '?'))
            {
                index = Tag(text, index, tokens);
                continue;
            }

            index++;
        }

        return tokens;
    }

    private static int Tag(string text, int start, List<CodeToken> tokens)
    {
        var index = start + 1;
        while (index < text.Length && text[index] is '/' or '!' or '?')
        {
            index++;
        }

        tokens.Add(new(start, index - start, CodeTokenKind.Punctuation));
        var nameEnd = NameEnd(text, index);
        if (nameEnd > index)
        {
            tokens.Add(new(index, nameEnd - index, CodeTokenKind.Keyword));
        }

        index = nameEnd;

        while (index < text.Length && text[index] is not '>')
        {
            if (text[index] is '"' or '\'')
            {
                var close = text.IndexOf(text[index], index + 1);
                var end = close < 0 ? text.Length : close + 1;
                tokens.Add(new(index, end - index, CodeTokenKind.Quoted));
                index = end;
            }
            else if (char.IsLetter(text[index]) || text[index] is '_' or ':' or '@')
            {
                var end = NameEnd(text, index);
                tokens.Add(new(index, end - index, CodeTokenKind.Key));
                index = end;
            }
            else if (text[index] is '=' or '/' or '?')
            {
                tokens.Add(new(index, 1, CodeTokenKind.Punctuation));
                index++;
            }
            else
            {
                index++;
            }
        }

        if (index < text.Length)
        {
            tokens.Add(new(index, 1, CodeTokenKind.Punctuation));
            index++;
        }

        return index;
    }

    private static int NameEnd(string text, int index)
    {
        while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] is '-' or '_' or ':' or '.' or '@'))
        {
            index++;
        }

        return index;
    }

    #endregion

    #region Nested Types

    // One pass over the text with one grammar: at each place, the first of comment, string, number, word, variable,
    // or punctuation that starts there is read whole.
    private sealed class Scanner(string text, CodeGrammar grammar)
    {
        #region Private Fields

        private readonly List<CodeToken> tokens = [];

        private int index;

        #endregion

        #region Public Methods

        public List<CodeToken> Read()
        {
            while (index < text.Length)
            {
                var start = index;
                var kind = Next();

                if (kind is { } read && index > start)
                {
                    Add(start, index - start, read);
                }
                else if (index == start)
                {
                    index++;
                }
            }

            return tokens;
        }

        #endregion

        #region Reading

        private CodeTokenKind? Next()
        {
            var character = text[index];

            if (LineComment() || BlockComment())
            {
                return CodeTokenKind.Comment;
            }

            if (StringStarts())
            {
                return KeyOrString();
            }

            if (grammar.Variables && character is '$' && index + 1 < text.Length && (char.IsLetter(text[index + 1]) || text[index + 1] is '_' or '{'))
            {
                Variable();
                return CodeTokenKind.Key;
            }

            if (char.IsDigit(character) && !IsWordCharacter(Before()))
            {
                Number();
                return CodeTokenKind.Number;
            }

            if (char.IsLetter(character) || character is '_' or '@' || (grammar.HyphenatedNames && character is '!'))
            {
                return Word();
            }

            if (PunctuationCharacters.Contains(character))
            {
                while (index < text.Length && PunctuationCharacters.Contains(text[index]) && !OpensComment(index))
                {
                    index++;
                }

                return CodeTokenKind.Punctuation;
            }

            return null;
        }

        private bool LineComment()
        {
            if (!CommentStartsAt(index))
            {
                return false;
            }

            var end = text.IndexOf('\n', index);
            index = end < 0 ? text.Length : end;
            return true;
        }

        private bool CommentStartsAt(int at)
        {
            var opener = grammar.LineComments.FirstOrDefault(opener => text.AsSpan(at).StartsWith(opener, StringComparison.Ordinal));
            return opener is not null && (!grammar.CommentAfterSpace || at == 0 || char.IsWhiteSpace(text[at - 1]));
        }

        private bool OpensComment(int at) =>
            CommentStartsAt(at) || (grammar.BlockComment is var (open, _) && text.AsSpan(at).StartsWith(open, StringComparison.Ordinal));

        private bool BlockComment()
        {
            if (grammar.BlockComment is not var (open, close) || !text.AsSpan(index).StartsWith(open, StringComparison.Ordinal))
            {
                return false;
            }

            var end = text.IndexOf(close, index + open.Length, StringComparison.Ordinal);
            index = end < 0 ? text.Length : end + close.Length;
            return true;
        }

        private bool StringStarts() =>
            grammar.Quotes.Contains(text[index])
            || (grammar.PrefixedStrings && text[index] is '@' or '$' && QuoteAfterPrefix() > index);

        // A string, or a key where a colon follows it.
        private CodeTokenKind KeyOrString()
        {
            if (grammar.PrefixedStrings && text[index] is '@' or '$')
            {
                PrefixedString();
                return CodeTokenKind.Quoted;
            }

            var quote = text[index];

            if (grammar.TripleQuotes && text.AsSpan(index).StartsWith(new string(quote, 3), StringComparison.Ordinal))
            {
                var close = text.IndexOf(new string(quote, 3), index + 3, StringComparison.Ordinal);
                index = close < 0 ? text.Length : close + 3;
                return CodeTokenKind.Quoted;
            }

            index++;
            var multiline = grammar.MultilineQuotes.Contains(quote);

            // A backslash escapes the next character, except in a shell's single quotes, which hold it as written.
            var escapes = !(grammar.Variables && quote is '\'');

            while (index < text.Length && text[index] != quote && (multiline || text[index] is not '\n'))
            {
                index += escapes && text[index] is '\\' ? 2 : 1;
            }

            index = Math.Min(text.Length, index + 1);
            return grammar.KeyBeforeColon && ColonFollows() ? CodeTokenKind.Key : CodeTokenKind.Quoted;
        }

        // C#'s @"..." and $@"...": a doubled quote is a quote, and a verbatim string runs over line breaks.
        private void PrefixedString()
        {
            var verbatim = false;
            while (text[index] is '@' or '$')
            {
                verbatim |= text[index] is '@';
                index++;
            }

            index++;

            while (index < text.Length)
            {
                if (text[index] is '"')
                {
                    if (verbatim && index + 1 < text.Length && text[index + 1] is '"')
                    {
                        index += 2;
                        continue;
                    }

                    index++;
                    return;
                }

                if (!verbatim && text[index] is '\n')
                {
                    return;
                }

                index += !verbatim && text[index] is '\\' ? 2 : 1;
            }

            index = text.Length;
        }

        private int QuoteAfterPrefix()
        {
            var at = index;
            while (at < text.Length && text[at] is '@' or '$')
            {
                at++;
            }

            return at < text.Length && text[at] is '"' && at - index <= 2 ? at : -1;
        }

        private void Variable()
        {
            index++;

            if (text[index] is '{')
            {
                var close = text.IndexOf('}', index);
                index = close < 0 ? text.Length : close + 1;
                return;
            }

            while (index < text.Length && IsWordCharacter(text[index]))
            {
                index++;
            }
        }

        private void Number()
        {
            while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] is '.' or '_' or '%')
                && !(text[index] is '.' && (index + 1 >= text.Length || !char.IsDigit(text[index + 1]))))
            {
                index++;
            }
        }

        // A keyword, a literal word, a key before a colon, or a name drawn plain.
        private CodeTokenKind? Word()
        {
            var start = index;
            index++;

            while (index < text.Length && (IsWordCharacter(text[index]) || (grammar.HyphenatedNames && text[index] is '-')))
            {
                index++;
            }

            var word = text[start..index];

            return word switch
            {
                _ when grammar.Keywords.Contains(word) => CodeTokenKind.Keyword,
                _ when grammar.Literals.Contains(word) => CodeTokenKind.Number,
                _ when grammar.KeyBeforeColon && StartsLine(start) && ColonFollows() => CodeTokenKind.Key,
                _ => null,
            };
        }

        // A key stands first on its line, after indentation or a list's dash, so "a: b: c" keys only "a".
        private bool StartsLine(int at)
        {
            var before = at - 1;
            while (before >= 0 && text[before] is ' ' or '\t' or '-')
            {
                before--;
            }

            return before < 0 || text[before] is '\n' or '{' or ';';
        }

        private bool ColonFollows()
        {
            var at = index;
            while (at < text.Length && text[at] is ' ' or '\t')
            {
                at++;
            }

            return at < text.Length && text[at] is ':' && (at + 1 >= text.Length || text[at + 1] is not ':');
        }

        private char Before() => index > 0 ? text[index - 1] : ' ';

        private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character is '_';

        private void Add(int start, int length, CodeTokenKind kind)
        {
            if (tokens is [.., var last] && last.Kind == kind && last.Start + last.Length == start && kind is CodeTokenKind.Punctuation)
            {
                tokens[^1] = last with { Length = last.Length + length };
                return;
            }

            tokens.Add(new(start, length, kind));
        }

        #endregion
    }

    #endregion
}

/// <summary>What a part of a block of code is, which decides its color and weight.</summary>
public enum CodeTokenKind
{
    Keyword,

    /// <summary>A string or other quoted text.</summary>
    Quoted,
    Comment,
    Number,
    Key,
    Punctuation,
}

/// <summary>A part of a block of code drawn apart from plain text: where it starts, how long it is, and what it is.</summary>
public sealed record CodeToken(int Start, int Length, CodeTokenKind Kind);
