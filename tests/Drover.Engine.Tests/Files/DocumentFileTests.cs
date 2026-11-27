namespace Drover.Engine.Tests.Files;

/// <summary>
/// PDF and office documents a person attaches: the model is given the text the server reads out of them, a document
/// cut at a limit says so to the model and to the person, a scanned PDF reaches a model that reads pictures as
/// pictures of its pages, and a PDF within every bound reaches a model that reads PDFs as itself.
/// </summary>
public sealed class DocumentFileTests(PostgresServer postgres)
{
    #region Read Out For The Model

    [Fact]
    public async Task A_text_pdf_is_answered_from_its_content_by_a_bot_with_no_sandbox()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("It is 1,250.00.")]));
        var threadId = await world.NewThread();
        var upload = await world.Upload(threadId, "invoice.pdf", TestDocuments.Pdf("Invoice 4471", "Total due 1,250.00"));
        upload.File.Should().NotBeNull(upload.Detail);
        upload.File!.ContentType.Should().Be(DocumentTypes.Pdf);
        upload.File.ReadingNote.Should().BeNull("the bot reads all of it");
        world.StartHost();

        var runId = await PostWith(world, threadId, "What is the total?", upload.File.Id);

        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);
        run.Status.Should().Be(RunStatus.Completed);
        run.Tainted.Should().BeFalse("a person's own document is not recorded as an untrusted read");
        var text = TextsOf(world.Model.Requests[0]).Should().ContainSingle(found => found.Contains("invoice.pdf", StringComparison.Ordinal)).Which;
        text.Should().StartWith("[The person attached invoice.pdf, a PDF document of 2 pages. Its text follows.]")
            .And.Contain("[Page 1]\nInvoice 4471").And.Contain("[Page 2]\nTotal due 1,250.00").And.EndWith("[End of invoice.pdf.]");
        world.Model.ImagesSeen[0].Should().BeEmpty();
    }

    [Fact]
    public async Task A_word_document_is_answered_from_its_content()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("On the first.")]));
        var threadId = await world.NewThread();
        var upload = await world.Upload(threadId, "lease.docx", TestDocuments.Docx("Lease agreement", "Rent is due on the first of each month."));
        upload.File!.ContentType.Should().Be(DocumentTypes.Word);
        world.StartHost();

        var runId = await PostWith(world, threadId, "When is rent due?", upload.File.Id);

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        TextsOf(world.Model.Requests[0]).Should().Contain(
            "[The person attached lease.docx, a Word document. Its text follows.]\nLease agreement\nRent is due on the first of each month.\n[End of lease.docx.]");
    }

    [Fact]
    public async Task A_workbook_and_a_presentation_reach_the_model_as_their_text()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Read them.")]));
        var threadId = await world.NewThread();
        var workbook = await world.Upload(threadId, "budget.xlsx", TestDocuments.Xlsx(("Budget", [["Rent", "1200"]])));
        var deck = await world.Upload(threadId, "review.pptx", TestDocuments.Pptx("Quarterly review", "Sales grew 8%"));
        world.StartHost();

        var post = await world.PostRaw(threadId, "Summarize both", fileIds: [workbook.File!.Id, deck.File!.Id]);
        (await world.WaitForStatus(post.Run!.Id, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var texts = TextsOf(world.Model.Requests[0]).ToList();
        texts.Should().Contain(text => text.StartsWith("[The person attached budget.xlsx, an Excel workbook of 1 sheet.", StringComparison.Ordinal)
            && text.Contains("Rent\t1200", StringComparison.Ordinal));
        texts.Should().Contain(text => text.StartsWith("[The person attached review.pptx, a PowerPoint presentation of 2 slides.", StringComparison.Ordinal)
            && text.Contains("[Slide 2]\nSales grew 8%", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_document_is_read_out_and_also_put_in_the_sandbox_when_the_bot_may_use_one()
    {
        var sandbox = new FakeSandboxProvider();
        await using var world = await SandboxWorld(sandbox, new ScriptedModel([new TextOutput("Got it.")]));
        var threadId = await world.NewSharedThread();
        var pdf = TestDocuments.Pdf("Quarterly numbers");
        var upload = await world.Upload(threadId, "report.pdf", pdf);
        world.StartHost();

        var runId = await PostWith(world, threadId, "Read the report", upload.File!.Id);

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        var path = $"attachments/{upload.File.Id:N}/report.pdf";
        sandbox.Only().Files[path].Should().Equal(pdf);
        TextsOf(world.Model.Requests[0]).Should().Contain(text => text.Contains("Quarterly numbers", StringComparison.Ordinal)
            && text.Contains($"in your sandbox at {path}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_document_in_a_private_conversation_of_a_shared_space_is_read_out_but_kept_out_of_the_sandbox()
    {
        var sandbox = new FakeSandboxProvider();
        await using var world = await SandboxWorld(sandbox, new ScriptedModel([new TextOutput("Got it.")]));
        await MakeShared(world);
        var threadId = await world.NewThread();
        var upload = await world.Upload(threadId, "private.docx", TestDocuments.Docx("Only for me"));
        upload.File.Should().NotBeNull("the bot reads a document's text without a sandbox");
        world.StartHost();

        var runId = await PostWith(world, threadId, "Read it", upload.File!.Id);

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        sandbox.Created.Should().Be(0, "others in the space can reach the sandbox");
        TextsOf(world.Model.Requests[0]).Should().Contain(text => text.Contains("Only for me", StringComparison.Ordinal));
    }

    #endregion

    #region Cut With A Note

    [Fact]
    public async Task A_document_past_MaxDocumentPages_is_cut_with_a_note_to_the_model_and_to_the_person()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Read two pages.")]), everyHost: services =>
            services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions { Rules = new DeploymentPolicy { MaxDocumentPages = 2 } })));
        var threadId = await world.NewThread();
        var upload = await world.Upload(threadId, "book.pdf", TestDocuments.Pdf("Chapter one", "Chapter two", "Chapter three"));
        world.StartHost();

        var post = await world.PostRaw(threadId, "Summarize", fileIds: [upload.File!.Id]);
        (await world.WaitForStatus(post.Run!.Id, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        const string Note = "A bot reads only its first 2 of its 3 pages.";
        upload.File.ReadingNote.Should().Be(Note);
        upload.File.ToResponse().Caption.Should().Be(Note, "the composer's card shows it");
        post.Message!.Parts.OfType<FilePart>().Single().Caption.Should().Be(Note, "the conversation shows it under the file");
        var text = TextsOf(world.Model.Requests[0]).Single(found => found.Contains("book.pdf", StringComparison.Ordinal));
        text.Should().Contain("Chapter two").And.NotContain("Chapter three");
        text.Should().EndWith("[book.pdf continues. Only its first 2 of its 3 pages are shown.]");
    }

    [Fact]
    public async Task A_document_past_the_character_bound_is_cut_with_a_note_to_the_model_and_to_the_person()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Long.")]));
        var threadId = await world.NewThread();
        var upload = await world.Upload(threadId, "long.docx", TestDocuments.Docx(new string('a', RunFiles.MaxTextCharacters), "the tail"));
        world.StartHost();

        var post = await world.PostRaw(threadId, "Summarize", fileIds: [upload.File!.Id]);
        (await world.WaitForStatus(post.Run!.Id, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        upload.File.ReadingNote.Should().Be("A bot reads only its first 100,000 characters.");
        post.Message!.Parts.OfType<FilePart>().Single().Caption.Should().Be("A bot reads only its first 100,000 characters.");
        var text = TextsOf(world.Model.Requests[0]).Single(found => found.Contains("long.docx", StringComparison.Ordinal));
        text.Should().Contain(new string('a', RunFiles.MaxTextCharacters)).And.NotContain("the tail");
        text.Should().EndWith($"[long.docx continues. Only its first {RunFiles.MaxTextCharacters} characters are shown.]");
    }

    [Fact]
    public async Task A_damaged_document_is_kept_with_a_note_and_the_model_is_told_it_could_not_be_read()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("I cannot read it.")]));
        var threadId = await world.NewThread();
        var upload = await world.Upload(threadId, "report.pdf", [.. "%PDF-1.7\n"u8, 0, 1, 2, 3]);
        world.StartHost();

        var runId = await PostWith(world, threadId, "Read the report", upload.File!.Id);

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        upload.File.ReadingNote.Should().Be("A bot cannot read it: it is damaged, or not the kind of file its name says.");
        TextsOf(world.Model.Requests[0]).Should().Contain(text =>
            text.Contains("report.pdf", StringComparison.Ordinal) && text.Contains("it could not be read: it is damaged", StringComparison.Ordinal));
    }

    #endregion

    #region Scanned

    [Fact]
    public async Task A_scanned_pdf_reaches_a_model_that_reads_pictures_as_pictures_of_its_pages()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("A signed form.")]));
        await SetVision(world, true);
        var threadId = await world.NewThread();
        var first = TestDocuments.Jpeg(30, 40, 0x01);
        var second = TestDocuments.Jpeg(30, 40, 0x02);
        var upload = await world.Upload(threadId, "scan.pdf", TestDocuments.ScannedPdf(first, second));
        world.StartHost();

        var runId = await PostWith(world, threadId, "What is this?", upload.File!.Id);

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        upload.File.ReadingNote.Should().Be("It has no text, so a bot sees its first pages as pictures.");
        world.Model.ImagesSeen[0].Select(image => (image.MediaType, image.Data.ToArray()))
            .Should().BeEquivalentTo([("image/jpeg", first), ("image/jpeg", second)], options => options.WithStrictOrdering());
        TextsOf(world.Model.Requests[0]).Should().Contain(
            "[The person attached scan.pdf, a PDF document of 2 pages. It has no text, so pictures of its pages follow, from the first.]");
    }

    [Fact]
    public async Task A_scanned_pdf_for_a_model_that_cannot_read_pictures_is_said_to_have_no_text()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Nothing to read.")]));
        var threadId = await world.NewThread();
        var upload = await world.Upload(threadId, "scan.pdf", TestDocuments.ScannedPdf(TestDocuments.Jpeg(30, 40, 1)));
        world.StartHost();

        var runId = await PostWith(world, threadId, "What is this?", upload.File!.Id);

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        upload.File.ReadingNote.Should().Be("It has no text a bot can read.");
        world.Model.ImagesSeen[0].Should().BeEmpty();
        TextsOf(world.Model.Requests[0]).Should().Contain(text => text.Contains("scan.pdf", StringComparison.Ordinal)
            && text.Contains("it has no text, and no picture of its pages could be given to you", StringComparison.Ordinal));
    }

    #endregion

    #region As A PDF

    [Fact]
    public async Task A_pdf_reaches_a_model_that_reads_pdfs_as_the_pdf_itself()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("It is 1,250.00.")]));
        await SetCapabilities(world, new ModelCapabilities { PdfInput = true, PdfMaxPages = 100, PdfMaxBytes = 32 * 1024 * 1024 });
        var threadId = await world.NewThread();
        var pdf = TestDocuments.Pdf("Invoice 4471", "Total due 1,250.00");
        var upload = await world.Upload(threadId, "invoice.pdf", pdf);
        world.StartHost();

        var runId = await PostWith(world, threadId, "What is the total?", upload.File!.Id);

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        world.Model.ImagesSeen[0].Select(data => (data.MediaType, data.Data.ToArray()))
            .Should().BeEquivalentTo([(DocumentTypes.Pdf, pdf)]);
        var texts = TextsOf(world.Model.Requests[0]).ToList();
        texts.Should().Contain("[The person attached invoice.pdf, a PDF of 2 pages:]");
        texts.Should().NotContain(text => text.Contains("Total due", StringComparison.Ordinal), "the model reads the PDF itself, not its text");
    }

    [Theory]
    [InlineData(1, 100, 32 * 1024 * 1024)]
    [InlineData(100, 1, 32 * 1024 * 1024)]
    [InlineData(100, 100, 64)]
    public async Task A_pdf_past_the_servers_or_the_providers_bound_is_given_as_its_text(int serverPages, int providerPages, long providerBytes)
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Read.")]), everyHost: services =>
            services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions { Rules = new DeploymentPolicy { MaxDocumentPages = serverPages } })));
        await SetCapabilities(world, new ModelCapabilities { PdfInput = true, PdfMaxPages = providerPages, PdfMaxBytes = providerBytes });
        var threadId = await world.NewThread();
        var upload = await world.Upload(threadId, "invoice.pdf", TestDocuments.Pdf("Invoice 4471", "Total due 1,250.00"));
        world.StartHost();

        var runId = await PostWith(world, threadId, "What is the total?", upload.File!.Id);

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        world.Model.ImagesSeen[0].Should().BeEmpty();
        TextsOf(world.Model.Requests[0]).Should().Contain(text => text.StartsWith("[The person attached invoice.pdf, a PDF document of 2 pages.", StringComparison.Ordinal)
            && text.Contains("Invoice 4471", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_word_document_reaches_a_model_that_reads_pdfs_as_its_text()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("On the first.")]));
        await SetCapabilities(world, new ModelCapabilities { PdfInput = true, PdfMaxPages = 100, PdfMaxBytes = 32 * 1024 * 1024 });
        var threadId = await world.NewThread();
        var upload = await world.Upload(threadId, "lease.docx", TestDocuments.Docx("Rent is due on the first."));
        world.StartHost();

        var runId = await PostWith(world, threadId, "When is rent due?", upload.File!.Id);

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        world.Model.ImagesSeen[0].Should().BeEmpty();
        TextsOf(world.Model.Requests[0]).Should().Contain(text => text.Contains("Rent is due on the first.", StringComparison.Ordinal));
    }

    #endregion

    #region Private Methods

    private static Task<bool> SetCapabilities(RunWorld world, ModelCapabilities capabilities) =>
        world.Read(async db =>
        {
            var model = await db.ModelCatalog.SingleAsync(entry => entry.Model == "scripted", RunWorld.Token);
            model.Capabilities = capabilities;
            return true;
        });

    private static IEnumerable<string> TextsOf(ModelChatRequest request) =>
        request.Messages.SelectMany(message => message.Contents).OfType<ModelText>().Select(text => text.Text);

    private static async Task<Guid> PostWith(RunWorld world, Guid threadId, string text, Guid fileId)
    {
        var post = await world.PostRaw(threadId, text, fileIds: [fileId]);
        post.Problem.Should().Be(PostProblem.None, post.Detail);
        return post.Run!.Id;
    }

    private static async Task<Guid> MakeShared(RunWorld world)
    {
        (await world.WithService<AccountService, bool>(accounts => accounts.MakeShared(world.SpaceId, world.UserId, RunWorld.Token))).Should().BeTrue();
        return await world.AddMember(MembershipRole.Admin);
    }

    private static Task<bool> SetVision(RunWorld world, bool vision) =>
        world.Read(async db =>
        {
            var model = await db.ModelCatalog.SingleAsync(entry => entry.Model == "scripted", RunWorld.Token);
            model.Capabilities = new ModelCapabilities { Vision = vision };
            return true;
        });

    private async Task<RunWorld> SandboxWorld(FakeSandboxProvider provider, ScriptedModel script)
    {
        var world = await RunWorld.Create(postgres, script, everyHost: services =>
        {
            services.AddSingleton<ISandboxProvider>(provider);
            services.AddSandboxTools();
        });
        await world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.SandboxPolicy = SandboxPolicy.Private;
            return true;
        });
        await world.Grant("sandbox.read_file", GrantEffect.Allow);
        return world;
    }

    #endregion
}
