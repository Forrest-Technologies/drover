namespace Drover.Engine.Tests.Voice;

public sealed class SpeechTextTests
{
    #region Splitting

    [Fact]
    public void Text_that_fits_is_one_piece_and_blank_text_is_none()
    {
        SpeechText.Split("  Hello there.  ", 200).Should().Equal("Hello there.");
        SpeechText.Split("   ", 200).Should().BeEmpty();
    }

    [Fact]
    public void Long_text_is_cut_at_the_last_sentence_end_that_fits()
    {
        var pieces = SpeechText.Split("One two three. Four five six! Seven eight nine? Ten.", 32);

        pieces.Should().Equal("One two three. Four five six!", "Seven eight nine? Ten.");
        pieces.Should().OnlyContain(piece => piece.Length <= 32);
    }

    [Fact]
    public void A_full_stop_inside_a_number_or_a_name_does_not_end_a_sentence()
    {
        SpeechText.Split("It costs 3.50 at example.org today and more later on", 30)
            .Should().Equal("It costs 3.50 at example.org", "today and more later on");
    }

    [Fact]
    public void A_sentence_longer_than_a_piece_is_cut_at_a_space_and_a_word_longer_than_a_piece_in_the_middle()
    {
        SpeechText.Split("alpha beta gamma delta epsilon", 12).Should().Equal("alpha beta", "gamma delta", "epsilon");
        SpeechText.Split(new string('x', 25), 10).Should().Equal(new string('x', 10), new string('x', 10), new string('x', 5));
    }

    [Fact]
    public void A_line_break_ends_a_piece_and_a_character_of_two_halves_is_never_cut_in_two()
    {
        SpeechText.Split("First line\nSecond line that runs on", 20).Should().Equal("First line", "Second line that", "runs on");

        var pieces = SpeechText.Split($"{new string('a', 9)}😀{new string('b', 9)}", 10);

        pieces.All(piece => !char.IsHighSurrogate(piece[^1]) && !char.IsLowSurrogate(piece[0])).Should().BeTrue();
        string.Concat(pieces).Should().Be($"{new string('a', 9)}😀{new string('b', 9)}");
    }

    [Fact]
    public void Nothing_is_lost_or_reordered_but_the_space_between_pieces()
    {
        var text = string.Join(' ', Enumerable.Range(1, 400).Select(number => $"Sentence number {number} ends here."));

        var pieces = SpeechText.Split(text, SpeechText.ShortPieceCharacters);

        string.Join(' ', pieces).Should().Be(text);
        pieces.All(piece => piece.Length is > 0 and <= SpeechText.ShortPieceCharacters).Should().BeTrue();
    }

    #endregion

    #region Providers

    [Fact]
    public void One_provider_takes_short_pieces_and_answers_only_in_wav_and_several_pieces_elsewhere_are_mp3()
    {
        var shortPieces = new Uri("https://api.groq.com/openai/v1");
        var other = new Uri("https://api.openai.com/v1");

        SpeechText.PieceCharactersFor(shortPieces).Should().Be(200);
        SpeechText.PieceCharactersFor(other).Should().Be(4000);
        VoiceService.ChooseFormat(SpeechFormat.Mp3, shortPieces, 1).Should().Be(SpeechFormat.Wav);
        VoiceService.ChooseFormat(SpeechFormat.Opus, shortPieces, 3).Should().Be(SpeechFormat.Wav);
        VoiceService.ChooseFormat(SpeechFormat.Wav, other, 1).Should().Be(SpeechFormat.Wav);
        VoiceService.ChooseFormat(SpeechFormat.Wav, other, 2).Should().Be(SpeechFormat.Mp3, "WAV answers cannot simply be put end to end");
        VoiceService.ChooseFormat(SpeechFormat.Pcm, other, 2).Should().Be(SpeechFormat.Mp3);
    }

    #endregion
}
