namespace Drover.Adapters.Tests.Voice;

/// <summary>The server's own speech service is the same protocol on a client of its own, so it passes the same suite.</summary>
public sealed class OperatorSpeechConformanceTests : SpeechConformance
{
    #region Adapter Hooks

    protected override SpeechUnderTest Start(string transcript, byte[] audio, IReadOnlyList<string> voices) =>
        Subject(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.EndsWith("/audio/transcriptions", StringComparison.Ordinal) =>
                ScriptedHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { text = transcript })),
            var path when path.EndsWith("/audio/speech", StringComparison.Ordinal) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekableStream(new MemoryStream(audio))) },
            var path when path.EndsWith("/audio/voices", StringComparison.Ordinal) && voices.Count > 0 =>
                ScriptedHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { voices = voices.Select(id => new { id, name = id }) })),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

    protected override SpeechUnderTest StartFailing(int status, string providerText) =>
        Subject(_ => ScriptedHandler.Json((HttpStatusCode)status, JsonSerializer.Serialize(new { error = new { message = providerText } })));

    #endregion

    #region Kind

    [Fact]
    public void Its_kind_is_the_servers_own_and_never_a_stored_connections()
    {
        var speech = new OperatorSpeech(new FixtureHttpClientFactory(new ScriptedHandler((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))));

        ((ISpeechToText)speech).Kind.Should().Be(ModelConnectionKinds.ServerSpeech);
        ((ITextToSpeech)speech).FixedAddress.Should().BeNull("the operator's configuration supplies the address");
        ModelConnectionKinds.ServerSpeech.Should().NotBe(ModelConnectionKinds.Compatible);
    }

    #endregion

    #region Private Methods

    private static SpeechUnderTest Subject(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var handler = new ScriptedHandler((request, _, _) => Task.FromResult(answer(request)));
        var speech = new OperatorSpeech(new FixtureHttpClientFactory(handler));
        return new SpeechUnderTest(
            speech,
            speech,
            new ModelEndpoint(OperatorSpeech.ConnectionKind, CompatibleRig.Address),
            TestKeys.Generate(),
            "speech-model",
            () => handler.Count);
    }

    #endregion
}
