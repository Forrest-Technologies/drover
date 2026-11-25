namespace Drover.Adapters.Tests;

/// <summary>
/// The Web Push channel against recorded push service answers, offline. The device's keys are made here, so the test
/// reads the payload the way the device would: by decrypting what reached the push service (RFC 8291, aes128gcm).
/// </summary>
public sealed class PushPayloadTests : PushChannelConformance
{
    #region Urgency

    // A phone in deep idle is woken only by an urgent push; anything less waits for its next maintenance window. Each
    // wake-up is one the person sees, so each is urgent.
    [Theory]
    [InlineData(PushKinds.RunFinished)]
    [InlineData(PushKinds.RunFailed)]
    [InlineData(PushKinds.ApprovalWaiting)]
    [InlineData(PushKinds.DeviceTest)]
    public async Task Every_wake_up_is_sent_as_urgent_so_a_phone_in_deep_idle_wakes_for_it(string kind)
    {
        var rig = Rig(PushServiceAnswer.Accepted);

        await rig.Channel.Send(rig.Subscription, new PushWakeUp(kind, Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        rig.Received().Should().ContainSingle().Which.Headers.Should().Contain("Urgency", "high");
    }

    [Fact]
    public async Task Payload_NeverContainsMessageText()
    {
        var rig = Rig(PushServiceAnswer.Accepted);

        await rig.Channel.Send(
            rig.Subscription,
            new PushWakeUp(PushKinds.RunFinished, Guid.CreateVersion7()),
            TestContext.Current.CancellationToken);

        using var payload = JsonDocument.Parse(rig.Received().Should().ContainSingle().Subject.Payload);
        payload.RootElement.EnumerateObject().Select(property => property.Name).Should()
            .BeEquivalentTo(["kind", "thread", "user"]);
        payload.RootElement.TryGetProperty("body", out _).Should().BeFalse();
        payload.RootElement.TryGetProperty("botName", out _).Should().BeFalse();
        payload.RootElement.TryGetProperty("replyText", out _).Should().BeFalse();
    }

    #endregion

    #region Rig

    protected override PushRig Rig(PushServiceAnswer answer)
    {
        var device = new TestDevice();
        HttpMessageHandler handler = answer switch
        {
            PushServiceAnswer.Unreachable => new ScriptedHandler((_, _, _) => throw new HttpRequestException("No route to the push service.")),

            // As the checked outbound handler fails a connection it refuses: the refusal inside a request failure.
            PushServiceAnswer.Blocked => new ScriptedHandler((_, _, _) => throw new HttpRequestException(
                "The connection was refused.",
                new OutboundBlockedException("This server does not allow requests to private addresses."))),
            _ => new RecordedHandler(RecordedFixture.Load("web-push", FixtureFor(answer))),
        };
        var channel = new WebPushChannel(
            new FixtureHttpClientFactory(handler).CreateClient("web-push"),
            WebPushKeys.Generate("mailto:operator@example.invalid"),
            NullLogger<WebPushChannel>.Instance);
        return new PushRig(channel, device.Subscription, () => Received(handler, device));
    }

    #endregion

    #region Private Methods

    private static string FixtureFor(PushServiceAnswer answer) => answer switch
    {
        PushServiceAnswer.Accepted => "accepted",
        PushServiceAnswer.Gone => "gone",
        PushServiceAnswer.NotFound => "not_found",
        PushServiceAnswer.TooManyRequests => "too_many_requests",
        PushServiceAnswer.Unavailable => "unavailable",
        _ => "refused",
    };

    private static IReadOnlyList<ReceivedPush> Received(HttpMessageHandler handler, TestDevice device) =>
        handler is RecordedHandler recorded
            ? [.. recorded.Requests.Select(request => new ReceivedPush(
                request.Headers.Concat(request.ContentHeaders).ToDictionary(header => header.Key, header => header.Value),
                device.Decrypt(request.Bytes)))]
            : [];

    #endregion
}


/// <summary>A browser's side of a subscription: its key pair and secret, and the decryption a device does on arrival.</summary>
public sealed class TestDevice
{
    #region Private Fields

    private readonly ECDiffieHellman key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    private readonly byte[] authSecret = RandomNumberGenerator.GetBytes(16);

    private readonly Guid userId = Guid.CreateVersion7();

    #endregion

    #region Properties

    public PushSubscription Subscription => new()
    {
        UserId = userId,
        Endpoint = "https://push.example/send/device-1",
        PublicKey = Base64Url.EncodeToString(PublicKey),
        AuthSecret = Base64Url.EncodeToString(authSecret),
    };

    private byte[] PublicKey => Uncompressed(key.ExportParameters(false).Q);

    #endregion

    #region Public Methods

    /// <summary>One aes128gcm record: salt, record size, the sender's key, then the sealed text and its padding.</summary>
    public string Decrypt(byte[] body)
    {
        var salt = body[..16];
        var keyLength = body[20];
        var senderKey = body[21..(21 + keyLength)];
        var sealedText = body[(21 + keyLength)..];

        using var sender = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = senderKey[1..33], Y = senderKey[33..65] },
        });
        var shared = key.DeriveRawSecretAgreement(sender.PublicKey);
        byte[] keyInfo = [.. "WebPush: info\0"u8, .. PublicKey, .. senderKey];
        var inputKey = HKDF.Expand(HashAlgorithmName.SHA256, HKDF.Extract(HashAlgorithmName.SHA256, shared, authSecret), 32, keyInfo);
        var pseudoRandomKey = HKDF.Extract(HashAlgorithmName.SHA256, inputKey, salt);
        var contentKey = HKDF.Expand(HashAlgorithmName.SHA256, pseudoRandomKey, 16, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, pseudoRandomKey, 12, "Content-Encoding: nonce\0"u8.ToArray());

        var plain = new byte[sealedText.Length - 16];
        using var aes = new AesGcm(contentKey, 16);
        aes.Decrypt(nonce, sealedText[..^16], sealedText[^16..], plain);
        var end = Array.LastIndexOf(plain, (byte)2);
        return Encoding.UTF8.GetString(plain, 0, end);
    }

    #endregion

    #region Private Methods

    private static byte[] Uncompressed(ECPoint point) => [0x04, .. point.X!, .. point.Y!];

    #endregion
}
