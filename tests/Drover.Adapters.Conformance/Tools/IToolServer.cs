namespace Drover.Adapters.Conformance.Tools;

/// <summary>Which protocol generations a test tool server speaks.</summary>
public enum ServerGeneration
{
    /// <summary>The current generation only: no handshake, no session.</summary>
    Stateless,

    /// <summary>The older generation only: a handshake and a session. A request of the current generation is refused.</summary>
    SessionBased,

    /// <summary>Both on one address.</summary>
    Both,

    /// <summary>The older generation over the oldest transport, with its separate event stream.</summary>
    LegacySse,
}

/// <summary>How to stand a test tool server up. With a header named, a request without that exact header gets 401.</summary>
public sealed record ToolServerSpec(ServerGeneration Generation, string? HeaderName = null, string? HeaderValue = null);

/// <summary>
/// A tool server a conformance run talks to over real HTTP. It offers four tools: <c>echo</c> (says it only reads;
/// returns "echo: " and its <c>text</c> argument), <c>record</c> (says it is destructive; returns the structured
/// content <c>{"recorded":true}</c>), <c>fail</c> (runs and reports an error), and <c>slow</c> (never answers).
/// A call to a tool named <c>vanish</c> is taken, counted, and answered as if the server had lost the caller's
/// session: what a server that acted and then died, or a hostile one, looks like from outside.
/// </summary>
public interface IToolServer : IAsyncDisposable
{
    Uri Url { get; }

    /// <summary>How many handshakes the server has answered: one per session opened.</summary>
    int Handshakes { get; }

    /// <summary>How many calls to the <c>vanish</c> tool reached the server.</summary>
    int VanishedCalls { get; }

    /// <summary>Stops the server and starts it again on the same address, with every session forgotten.</summary>
    Task Restart();

    /// <summary>Stops the server. Its address then refuses connections.</summary>
    Task ShutDown();
}
