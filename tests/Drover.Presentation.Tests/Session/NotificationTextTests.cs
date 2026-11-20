namespace Drover.Presentation.Tests.Session;

public sealed class NotificationTextTests
{
    #region Plain Text

    [Fact]
    public void A_markup_name_stays_plain_until_a_markup_server_is_told()
    {
        const string Markup = """<a href="https://evil.example/login">Sign in</a> & 'Drover'""";

        NotificationText.Sanitize(Markup).Should().Be(Markup);
        NotificationText.EscapeMarkup(Markup).Should().Be(
            "&lt;a href=&quot;https://evil.example/login&quot;&gt;Sign in&lt;/a&gt; &amp; &apos;Drover&apos;");
        NotificationText.EscapeIfMarkup(Markup, bodyMarkup: false).Should().Be(Markup);
        NotificationText.UsesBodyMarkup(["body", "body-markup"]).Should().BeTrue();
        NotificationText.UsesBodyMarkup(["body"]).Should().BeFalse();
    }

    [Fact]
    public void A_newline_name_becomes_one_line()
    {
        NotificationText.Sanitize("Bot\r\nName\tTwo").Should().Be("Bot Name Two");
    }

    [Fact]
    public void A_bidirectional_override_name_loses_the_override()
    {
        NotificationText.Sanitize("ab\u202Ecd").Should().Be("abcd");
        NotificationText.Sanitize("left\u2067right").Should().Be("leftright");
    }

    [Fact]
    public void A_cut_stops_before_a_character_that_would_split()
    {
        var emoji = "\U0001F600";
        var body = $"{new string('a', 254)}{emoji}";

        var cut = NotificationText.Sanitize(body);

        cut.Should().Be(new string('a', 254));
        cut.Length.Should().Be(254);
        cut.Should().NotContain(emoji);
    }

    #endregion
}
