namespace Drover.Adapters.Tests.Support;

/// <summary>A network stand-in whose behavior is a function of the request and how many came before it.</summary>
public sealed class ScriptedHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> script)
    : HttpMessageHandler
{
    #region Private Fields

    private int count;

    #endregion

    #region Properties

    public int Count => Volatile.Read(ref count);

    #endregion

    #region Public Methods

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public const string ChatReply =
        """{"id":"c","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"ok"}}]}""";

    public const string ModelList = """{"object":"list","data":[{"id":"m","model_spec":{"name":"M","capabilities":{}}}]}""";

    #endregion

    #region Interface Implementations

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        script(request, Interlocked.Increment(ref count), cancellationToken);

    #endregion
}
