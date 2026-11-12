namespace Drover.Engine.Tests.Runs;

/// <summary>
/// The redactor replaces exact matches of every secret a run has revealed, and common token shapes. The values here
/// are made up for the test and are not credentials for anything.
/// </summary>
public sealed class RedactorTests
{
    #region Public Methods

    [Fact]
    public void A_revealed_secret_is_replaced_wherever_it_appears_in_json()
    {
        var secret = $"venice-{Convert.ToHexString(RandomNumberGenerator.GetBytes(12))}";
        var redactor = new Redactor([secret, null]);
        var json = JsonElement.Parse(
            """{"note":"key SECRET here","list":["SECRET",7,true,null],"SECRET":{"deep":"xSECRETy"}}""".Replace("SECRET", secret, StringComparison.Ordinal));

        var redacted = redactor.Redact(json).GetRawText();

        redacted.Should().NotContain(secret);
        redacted.Should().Be("""{"note":"key [redacted] here","list":["[redacted]",7,true,null],"[redacted]":{"deep":"x[redacted]y"}}""");
    }

    [Theory]
    [InlineData("sk-", 40)]
    [InlineData("ghp_", 36)]
    [InlineData("github_pat_", 30)]
    [InlineData("xoxb-", 24)]
    public void A_value_shaped_like_a_common_token_is_replaced_even_when_the_run_never_revealed_it(string prefix, int length)
    {
        var token = $"{prefix}{new string('a', length / 2)}{new string('7', length / 2)}";

        var redacted = new Redactor([]).Redact($"use {token} now");

        redacted.Should().Be("use [redacted] now");
    }

    [Fact]
    public void A_bearer_header_keeps_its_scheme_and_loses_its_value()
    {
        var value = Convert.ToHexString(RandomNumberGenerator.GetBytes(20));

        new Redactor([]).Redact($"Authorization: Bearer {value}").Should().Be("Authorization: Bearer [redacted]");
    }

    [Fact]
    public void A_private_key_block_is_replaced_whole()
    {
        // The markers are put together here, so the source holds nothing shaped like a key for a scanner to find.
        var label = string.Join(' ', "PRIVATE", "KEY");
        var block = $"-----BEGIN {label}-----\n{Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))}\n-----END {label}-----";

        new Redactor([]).Redact($"before {block} after").Should().Be("before [redacted] after");
    }

    [Fact]
    public void Ordinary_text_and_short_values_are_left_alone()
    {
        var redactor = new Redactor(["abc"]);

        redactor.Redact("The abc of it: ask the bearer of news, skip nothing.").Should().Be("The abc of it: ask the bearer of news, skip nothing.");
        redactor.Redact(JsonElement.Parse("""{"n":1.5,"ok":true,"text":"plain"}""")).GetRawText().Should().Be("""{"n":1.5,"ok":true,"text":"plain"}""");
    }

    #endregion
}
