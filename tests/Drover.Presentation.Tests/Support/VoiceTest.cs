namespace Drover.Presentation.Tests.Support;

/// <summary>
/// A test of voice: the fake server with the voice routes a test fills in, a signed-in person, a microphone and a
/// loudspeaker that are stand-ins, and a clock the test moves. Every model, voice and sentence is made up.
/// </summary>
public abstract class VoiceTest : IDisposable
{
    #region Constructors

    protected VoiceTest(string role = "owner")
    {
        Api = Server.CreateApi();
        Session = new(Api, new ShareInbox());
        Session.Begin(Samples.Session(role));
        Voice = new(Api, Session, NullLogger<SpaceVoice>.Instance);
        Reader = new(Voice, Api, Session, Playback, Toasts, NullLogger<ReadAloudViewModel>.Instance);
    }

    #endregion

    #region Properties

    protected static Guid ConnectionId { get; } = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c1");

    protected static string Space { get; } = $"/api/spaces/{Samples.Session().Spaces[0].Id}";

    protected static VoiceLimits Limits { get; } = new(25 * 1024 * 1024, 4000, null);

    protected FakeServer Server { get; } = new();

    protected DroverApi Api { get; }

    protected SessionState Session { get; }

    protected SpaceVoice Voice { get; }

    protected ReadAloudViewModel Reader { get; }

    protected FakeCapture Capture { get; } = new();

    protected FakePlayback Playback { get; } = new();

    protected FakeTimeProvider Time { get; } = new();

    protected ToastService Toasts { get; } = new(TimeProvider.System);

    protected IUiThread Ui { get; } = new AtOnce();

    #endregion

    #region Public Methods

    public void Dispose()
    {
        Reader.Dispose();
        Server.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>A slot one sample model of the space's own connection fills, chosen by the server.</summary>
    protected static VoiceSlotResponse Filled(string model, string? voice = null, bool automatic = true, bool onNetwork = false) => new(
        [new(ConnectionId, "Sample speech", model, false, onNetwork)],
        new(ConnectionId, "Sample speech", model, voice, false, onNetwork, automatic));

    protected static VoiceSlotResponse Empty { get; } = new([], null);

    /// <summary>The server answers the options route with these slots.</summary>
    protected void Offer(VoiceSlotResponse? listen = null, VoiceSlotResponse? speak = null, VoiceSlotResponse? live = null) =>
        Server.Answer("GET", $"{Space}/voice/options", new VoiceOptionsResponse(listen ?? Empty, speak ?? Empty, live ?? Empty, Limits));

    #endregion

    #region Nested Types

    // Work for the interface's thread runs where it is asked for: these tests have one thread of their own.
    private sealed class AtOnce : IUiThread
    {
        public void Post(Action work) => work();
    }

    #endregion
}
