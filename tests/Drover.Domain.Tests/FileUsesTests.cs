namespace Drover.Domain.Tests;

/// <summary>How each kind of file reaches a bot, by what the bot has.</summary>
public sealed class FileUsesTests
{
    #region Tests

    [Theory]
    [InlineData(FileKind.Image, true, false, FileUse.Image)]
    [InlineData(FileKind.Image, true, true, FileUse.Image)]
    [InlineData(FileKind.Image, false, true, FileUse.Sandbox)]
    [InlineData(FileKind.Image, false, false, FileUse.None)]
    [InlineData(FileKind.Text, false, false, FileUse.Text)]
    [InlineData(FileKind.Text, true, true, FileUse.Text)]
    [InlineData(FileKind.Other, true, true, FileUse.Sandbox)]
    [InlineData(FileKind.Other, true, false, FileUse.None)]
    public void Each_kind_goes_where_the_bot_can_open_it(FileKind kind, bool readsImages, bool hasSandbox, FileUse use) =>
        FileUses.For(kind, new FileAbilities(readsImages, hasSandbox)).Should().Be(use);

    [Fact]
    public void A_refusal_says_why_in_the_terms_of_the_kind()
    {
        var neither = new FileAbilities(false, false);

        FileUses.RefusalFor(FileKind.Image, neither).Should().Be(FileRefusals.ModelCannotReadImages);
        FileUses.RefusalFor(FileKind.Other, neither).Should().Be(FileRefusals.NothingCanOpenIt);
        FileUses.RefusalFor(FileKind.Text, neither).Should().BeNull();
    }

    #endregion
}
