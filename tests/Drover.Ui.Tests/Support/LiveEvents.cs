namespace Drover.Ui.Tests.Support;

/// <summary>
/// A server-sent event stream a test feeds one event at a time.
/// </summary>
public sealed class LiveEvents
{
    #region Private Fields

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    private readonly Pipe pipe = new();

    #endregion

    #region Public Methods

    public async Task Send<T>(T payload)
    {
        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes($"data: {JsonSerializer.Serialize(payload, json)}\n\n"));
        await pipe.Writer.FlushAsync();
    }

    public ValueTask End() => pipe.Writer.CompleteAsync();

    public HttpContent ToContent()
    {
        var content = new StreamContent(pipe.Reader.AsStream());
        content.Headers.ContentType = new("text/event-stream");
        return content;
    }

    #endregion
}
