namespace Drover.Adapters.Conformance.Images;

/// <summary>The shared wire and result contract for every image adapter.</summary>
public abstract class ImageGeneratorConformance
{
    #region Adapter Hooks

    protected abstract ImageGeneratorUnderTest Start();

    #endregion

    #region Generation

    [Fact]
    public async Task Generates_one_bounded_png_and_reports_usage()
    {
        var subject = Start();
        var request = new ModelImageRequest(
            "A red square on a plain background.",
            subject.Model,
            "1024x1024",
            1024 * 1024);

        var result = await subject.Generator.Generate(
            subject.Connection,
            subject.ApiKey,
            request,
            CancellationToken.None);

        result.Data.Span[..8].ToArray().Should().Equal([137, 80, 78, 71, 13, 10, 26, 10]);
        result.MediaType.Should().Be("image/png");
        result.Model.Should().Be(subject.Model);
        result.Usage.Should().Be(subject.ExpectedUsage);

        var sent = subject.SentRequests().Where(wire => wire.Method == "POST").Should().ContainSingle().Which;
        sent.Method.Should().Be("POST");
        sent.Uri.AbsolutePath.Should().EndWith("/images/generations");
        sent.Json.GetProperty("prompt").GetString().Should().Be(request.Prompt);
        sent.Json.GetProperty("model").GetString().Should().Be(request.Model);
        sent.Json.GetProperty("size").GetString().Should().Be(request.Size);
        sent.Json.GetProperty("n").GetInt32().Should().Be(1);
        sent.Json.GetProperty("output_format").GetString().Should().Be("png");
        sent.Json.TryGetProperty("response_format", out _).Should().BeFalse();
    }

    #endregion
}


/// <summary>An image adapter wired to one recorded response.</summary>
public sealed record ImageGeneratorUnderTest(
    IModelImageGenerator Generator,
    ModelEndpoint Connection,
    string ApiKey,
    string Model,
    ModelImageUsage ExpectedUsage,
    Func<IReadOnlyList<WireRequest>> SentRequests);
