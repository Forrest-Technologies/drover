namespace Drover.Ui.Chat;

/// <summary>The address fragment of one message, shared by the list that draws it and the links that point at it.</summary>
public static class MessageAnchors
{
    public static string Anchor(Guid messageId) => $"message-{messageId}";
}
