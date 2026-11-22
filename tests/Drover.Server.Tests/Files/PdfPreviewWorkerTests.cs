namespace Drover.Server.Tests.Files;

public sealed class PdfPreviewWorkerTests
{
    #region Public Methods

    [Fact]
    public async Task Kills_and_reaps_timed_out_worker_before_releasing_pdf_slot()
    {
        using var document = new PdfTest.Writer.PdfDocumentBuilder();
        var font = document.AddStandard14Font(PdfTest.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        document.AddPage(PdfTest.Content.PageSize.A4)
            .AddText("Safe content", 12, new PdfTest.Core.PdfPoint(50, 700), font);
        var pdf = document.Build();
        var userId = Guid.NewGuid();

        using var timedOutContent = new MemoryStream(pdf);
        var timedOut = await NativePreviewReader.Read(
            timedOutContent,
            CancellationToken.None,
            userId,
            TimeSpan.FromTicks(1),
            pdfParser: PdfPreviewWorker.Read);

        timedOut.Refusal.Should().Be(NativePreviewRefusal.TooComplex);

        using var safeContent = new MemoryStream(pdf);
        var safe = await NativePreviewReader.Read(
            safeContent,
            CancellationToken.None,
            userId,
            TimeSpan.FromSeconds(5),
            pdfParser: PdfPreviewWorker.Read);

        safe.Refusal.Should().BeNull();
        safe.Pages.Should().ContainSingle().Which.Text.Should().Be("Safe content");
    }

    #endregion
}
