namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// The shapes a secret value takes in text, and that each is found: whole, encoded inside a longer encoded run, in
/// part, and split across two pieces of a stream. Every value is generated for the test.
/// </summary>
public sealed class SecretScrubberTests
{
    #region Private Fields

    private const string Mark = "[redacted]";

    #endregion

    #region Public Methods

    public static TheoryData<string, Func<string, string>> Leaks() => new()
    {
        { "as it is", value => $"token={value}" },
        { "base64", value => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) },
        { "base64 after one byte", value => Convert.ToBase64String(Encoding.UTF8.GetBytes($"x{value}")) },
        { "base64 after two bytes", value => Convert.ToBase64String(Encoding.UTF8.GetBytes($"xy{value}")) },
        { "base64 in a longer run", value => Convert.ToBase64String(Encoding.UTF8.GetBytes($"KEY={value}&next=1")) },
        { "base64url", value => Convert.ToBase64String(Encoding.UTF8.GetBytes($"?{value}")).Replace('+', '-').Replace('/', '_') },
        { "hex", value => Convert.ToHexString(Encoding.UTF8.GetBytes(value)) },
        { "lowercase hex", value => Convert.ToHexString(Encoding.UTF8.GetBytes(value)).ToLowerInvariant() },
        { "percent-encoded", value => $"https://example.test/?k={Uri.EscapeDataString(value)}" },
        { "JSON", value => JsonSerializer.Serialize(new { key = value }) },
        { "reversed", value => string.Concat(value.Reverse()) },
        { "the first twenty characters", value => value[..20] },
        { "a middle part", value => $"…{value[10..26]}…" },
    };

    [Theory]
    [MemberData(nameof(Leaks))]
    public void Each_form_of_a_value_is_hidden(string form, Func<string, string> leak)
    {
        var value = NewValue();
        var scrubber = new SecretScrubber([value], Mark);

        var text = scrubber.Scrub(leak(value));

        text.Should().Contain(Mark, form);
        text.Should().NotContain(value[..SecretForms.PartLength], form);
    }

    [Fact]
    public void A_short_value_is_hidden_whole_and_encoded_but_not_in_part()
    {
        var value = "kq3Zp9Lx2W";
        var scrubber = new SecretScrubber([value], Mark);

        scrubber.Scrub($"a {value} b").Should().Be($"a {Mark} b");
        scrubber.Scrub(Convert.ToBase64String(Encoding.UTF8.GetBytes(value))).Should().Contain(Mark);
        SecretForms.Parts(value).Should().BeEmpty();
    }

    [Fact]
    public void Ordinary_text_is_left_alone()
    {
        var scrubber = new SecretScrubber([NewValue(), NewValue()], Mark);
        const string Text = "The deploy finished in 42 seconds. Nothing else changed; see https://example.test/status for details.";

        scrubber.Scrub(Text).Should().Be(Text);
    }

    [Fact]
    public void A_value_split_across_two_pieces_of_a_stream_is_caught()
    {
        var value = NewValue();
        var stream = new SecretScrubber([value], Mark).Stream();

        var out1 = stream.Add($"before {value[..7]}");
        var out2 = stream.Add($"{value[7..]} after");
        var rest = stream.Flush();

        $"{out1}{out2}{rest}".Should().Be($"before {Mark} after");
    }

    [Fact]
    public void A_placeholder_is_found_and_hashed_the_same_way_every_time()
    {
        var placeholder = BrokerTokens.NewPlaceholder();

        BrokerTokens.FindPlaceholders($"Authorization: Bearer {placeholder}, again {placeholder}").Should().Equal(placeholder);
        BrokerTokens.Hash(placeholder).Should().Equal(BrokerTokens.Hash(placeholder));
        BrokerTokens.FindPlaceholders("drover_ph_short").Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private static string NewValue() => $"test{Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant()}";

    #endregion
}
