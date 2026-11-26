namespace Drover.Domain.Models;

/// <summary>An estimate for request planning, independent of the provider's billing tokenizer.</summary>
public static class TokenEstimate
{
    #region Public Methods

    public static long Text(string? text) => ((text?.Length ?? 0) * 5L + 15) / 16;

    public static long Message(ModelChatMessage message) => 4 + message.Contents.Sum(content => content switch
    {
        ModelText text => Text(text.Text),
        ModelReasoning reasoning => Text(reasoning.Text) + Text(reasoning.ProtectedData),
        ModelToolCall call => Text(call.Name) + Text(call.CallId) + Text(call.Arguments.GetRawText()),
        ModelToolResult result => Text(result.CallId) + Text(result.Result.GetRawText()),
        ModelData or ModelUri => 1600L,
        _ => 0L,
    });

    public static long Request(ModelChatRequest request) =>
        request.Messages.Sum(Message) + Text(request.Options.Instructions) + Text(request.Options.ResponseSchema?.GetRawText()) +
        (request.Options.Tools?.Sum(tool => Text(tool.Name) + Text(tool.Description) + Text(tool.Schema.GetRawText())) ?? 0);

    public static double Calibration(long measured, long raw) => Math.Clamp((double)measured / Math.Max(1, raw), 0.5, 2);

    #endregion
}
