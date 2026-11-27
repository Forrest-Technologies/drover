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
    [InlineData(FileKind.Document, false, false, FileUse.Document)]
    [InlineData(FileKind.Document, true, true, FileUse.Document)]
    [InlineData(FileKind.Other, true, true, FileUse.Sandbox)]
    [InlineData(FileKind.Other, true, false, FileUse.None)]
    public void Each_kind_goes_where_the_bot_can_open_it(FileKind kind, bool readsImages, bool hasSandbox, FileUse use) =>
        FileUses.For(kind, new FileAbilities(readsImages, hasSandbox, AudienceCoversSandbox: true)).Should().Be(use);

    [Theory]
    [InlineData(FileKind.Image, true, FileUse.Image)]
    [InlineData(FileKind.Image, false, FileUse.None)]
    [InlineData(FileKind.Text, false, FileUse.Text)]
    [InlineData(FileKind.Document, false, FileUse.Document)]
    [InlineData(FileKind.Other, false, FileUse.None)]
    public void Nothing_goes_to_the_sandbox_from_a_conversation_others_who_reach_it_cannot_see(FileKind kind, bool readsImages, FileUse use) =>
        FileUses.For(kind, new FileAbilities(readsImages, HasSandbox: true, AudienceCoversSandbox: false)).Should().Be(use);

    [Theory]
    [InlineData(DocumentTypes.Pdf, true, FileUse.NativeDocument)]
    [InlineData(DocumentTypes.Pdf, false, FileUse.Document)]
    [InlineData(DocumentTypes.Word, true, FileUse.Document)]
    [InlineData("text/plain", true, FileUse.Text)]
    [InlineData("image/png", true, FileUse.Image)]
    public void A_pdf_goes_as_itself_only_to_a_model_that_reads_pdfs(string contentType, bool readsPdf, FileUse use) =>
        FileUses.For(contentType, new FileAbilities(true, HasSandbox: false, AudienceCoversSandbox: false, ModelReadsPdf: readsPdf))
            .Should().Be(use);

    [Fact]
    public void A_refusal_says_why_in_the_terms_of_the_kind()
    {
        var neither = new FileAbilities(false, false, AudienceCoversSandbox: true);
        var privately = new FileAbilities(false, true, AudienceCoversSandbox: false);

        FileUses.RefusalFor(FileKind.Image, neither).Should().Be(FileRefusals.ModelCannotReadImages);
        FileUses.RefusalFor(FileKind.Other, neither).Should().Be(FileRefusals.NothingCanOpenIt);
        FileUses.RefusalFor(FileKind.Text, neither).Should().BeNull();
        FileUses.RefusalFor(FileKind.Other, privately).Should().Be(FileRefusals.PrivateConversation);
        FileUses.RefusalFor(FileKind.Image, privately).Should().Be(FileRefusals.PrivateConversation);
        FileUses.RefusalFor(FileKind.Text, privately).Should().BeNull();
        FileUses.RefusalFor(FileKind.Document, neither).Should().BeNull();
        FileUses.RefusalFor(FileKind.Document, privately).Should().BeNull();
    }

    #endregion
}
