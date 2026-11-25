namespace Drover.App.Platforms.Android;

internal sealed record AndroidNoticeMessage(string Text, long SentAt, bool FromOwner, Guid? Id = null);


internal sealed record AndroidNoticePreview(string Title, string Body);


internal sealed record AndroidNotificationContent(
    string BotName,
    string Title,
    AndroidNoticeMessage[] Messages,
    string? Status = null,
    string? Draft = null,
    bool CanReply = false)
{
    #region Private Fields

    private const int HistoryLimit = 5;

    #endregion

    #region Public Methods

    public AndroidNoticePreview? PublicVersion(bool showOnLockScreen) =>
        showOnLockScreen ? null : new(BotName, "New reply");

    public AndroidNotificationContent Add(AndroidNoticeMessage message)
    {
        if (message.Id is { } id && Messages.Any(item => item.Id == id))
        {
            return this;
        }

        var ordered = Messages.Append(message).OrderBy(item => item.SentAt);
        AndroidNoticeMessage[] history = [.. ordered.TakeLast(HistoryLimit)];
        return this with { Messages = history };
    }

    #endregion
}


internal sealed record ConversationTarget(Guid Bot, Guid Thread, bool Main, Guid User, string Registration, Guid? Space)
{
    #region Properties

    public string Group
    {
        get
        {
            var scope = System.Text.Encoding.UTF8.GetBytes($"{User:N}/{Registration}");
            return $"drover-conversations:{Convert.ToHexString(SHA256.HashData(scope))}";
        }
    }

    public string Key => $"{Group}/{Space:N}/{Thread:N}";

    #endregion

    #region Public Methods

    public override string ToString() => nameof(ConversationTarget);

    #endregion
}
