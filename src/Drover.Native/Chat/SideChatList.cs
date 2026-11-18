namespace Drover.Native.Chat;

/// <summary>
/// The side chats with a bot, newest first: each a link to its conversation, with the bot's head lit while it
/// works there and a word for what the side chat has for the person.
/// </summary>
public sealed class SideChatList : Grid
{
    #region Private Fields

    private readonly StackPanel list = new() { Spacing = Tokens.Number("DvSpace1") };

    #endregion

    #region Constructors

    public SideChatList()
    {
        Children.Add(new ArrowKeyZone { Items = list });
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty ChatsProperty =
        Dp.Register<SideChatList, IReadOnlyList<SideChatItem>>(nameof(Chats), [], chats => chats.Show());

    public static readonly DependencyProperty SelectedIdProperty =
        Dp.Register<SideChatList, Guid?>(nameof(SelectedId), null, chats => chats.Show());

    public IReadOnlyList<SideChatItem> Chats
    {
        get => (IReadOnlyList<SideChatItem>)GetValue(ChatsProperty);
        set => SetValue(ChatsProperty, value);
    }

    public Guid? SelectedId
    {
        get => (Guid?)GetValue(SelectedIdProperty);
        set => SetValue(SelectedIdProperty, value);
    }

    #endregion

    #region Private Methods

    // A row is kept for as long as its side chat is listed, so the focus stays on it while the list changes around it.
    private void Show()
    {
        var kept = list.Children.OfType<FacetLink>().ToDictionary(link => (Guid)link.Tag);
        list.Children.Clear();

        foreach (var chat in Chats)
        {
            var link = kept.GetValueOrDefault(chat.Id) ?? new FacetLink { Tag = chat.Id };
            Describe(link, chat);
            list.Children.Add(link);
        }
    }

    private void Describe(FacetLink link, SideChatItem chat)
    {
        var avatar = new Avatar
        {
            BotName = chat.BotName,
            Hue = chat.BotHue,
            Size = 24,
            State = chat.Working ? AvatarState.Thinking : AvatarState.Rest,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var title = new TextBlock { Text = chat.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var attention = new AttentionNote { Attention = chat.Attention };

        var content = new Grid { ColumnSpacing = Tokens.Number("DvSpace3") };
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        SetColumn(title, 1);
        SetColumn(attention, 2);
        content.Children.Add(avatar);
        content.Children.Add(title);
        content.Children.Add(attention);

        var selected = chat.Id == SelectedId;
        link.Content = content;
        link.Address = chat.Href;
        link.Padding = Tokens.Space("DvSpace3", "DvSpace2");
        link.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary[selected ? "DvWeightLabel" : "DvWeightBody"];
        ArrowKeyZone.SetIsItem(link, true);
        link.ShowCurrent(selected);
        link.Refresh();
    }

    #endregion
}
