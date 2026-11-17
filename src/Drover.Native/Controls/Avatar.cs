namespace Drover.Native.Controls;

/// <summary>
/// A bot's character: a head in the bot's hue with a visor, built from the parts in <see cref="BotLook"/>. The
/// visor is the state: dark at rest, a slow breath when thinking, steady when writing, a bright bar sweeping across
/// when a tool runs, a pale red when failed. Each state is also said in words, in the avatar's name.
/// </summary>
public sealed class Avatar : ContentControl
{
    #region Private Fields

    private const int HueSteps = 12;

    private readonly AvatarCanvas canvas = new();

    #endregion

    #region Constructors

    public Avatar()
    {
        IsTabStop = false;
        Content = canvas;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Loaded += (_, _) => Update();
        Unloaded += (_, _) => canvas.StopMoving();
        ActualThemeChanged += (_, _) => Update();
        Update();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty BotNameProperty = Dp.Register<Avatar, string>(nameof(BotName), string.Empty, avatar => avatar.Update());

    public static readonly DependencyProperty HueProperty = Dp.Register<Avatar, int>(nameof(Hue), 0, avatar => avatar.Update());

    public static readonly DependencyProperty SeedProperty = Dp.Register<Avatar, uint?>(nameof(Seed), null, avatar => avatar.Update());

    public static readonly DependencyProperty SizeProperty = Dp.Register<Avatar, int>(nameof(Size), 32, avatar => avatar.Update());

    public static readonly DependencyProperty ImageUrlProperty = Dp.Register<Avatar, string?>(nameof(ImageUrl), null, avatar => avatar.Update());

    public static readonly DependencyProperty StateProperty =
        Dp.Register<Avatar, AvatarState>(nameof(State), AvatarState.Rest, avatar => avatar.Update());

    /// <summary>The bot's name. It is the avatar's accessible name. Named so because every element already has a <c>Name</c>.</summary>
    public string BotName
    {
        get => (string)GetValue(BotNameProperty);
        set => SetValue(BotNameProperty, value);
    }

    /// <summary>The identity hue, one of twelve fixed steps. Any number is folded into range. The head carries it.</summary>
    public int Hue
    {
        get => (int)GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }

    /// <summary>What picks the character's parts. The same seed always gives the same character.</summary>
    public uint? Seed
    {
        get => (uint?)GetValue(SeedProperty);
        set => SetValue(SeedProperty, value);
    }

    /// <summary>The edge length in pixels: 20, 24, 32, 48, or 96.</summary>
    public int Size
    {
        get => (int)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>An uploaded picture, shown in place of the character.</summary>
    public string? ImageUrl
    {
        get => (string?)GetValue(ImageUrlProperty);
        set => SetValue(ImageUrlProperty, value);
    }

    public AvatarState State
    {
        get => (AvatarState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    #endregion

    #region Private Methods

    protected override AutomationPeer OnCreateAutomationPeer() => new RolePeer(this, AutomationControlType.Image);

    private void Update()
    {
        Width = Height = Tokens.Number($"DvAvatar{Size}");
        AutomationProperties.SetName(this, AccessibleName());

        if (ImageUrl is { } address)
        {
            Content = new Border
            {
                CornerRadius = new(Tokens.Number("DvRadiusFull")),
                Background = new ImageBrush { ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(address)), Stretch = Stretch.UniformToFill },
            };
            canvas.StopMoving();
            return;
        }

        Content = canvas;
        canvas.Show(BotLook.From(Seed ?? BotLook.SeedFrom(BotName, Hue)), ((Hue % HueSteps) + HueSteps) % HueSteps, Size, State);
    }

    private string AccessibleName() => State switch
    {
        AvatarState.Thinking => $"{BotName}, thinking",
        AvatarState.Writing => $"{BotName}, writing",
        AvatarState.Tool => $"{BotName}, running a tool",
        AvatarState.Failed => $"{BotName}, failed",
        _ => BotName,
    };

    #endregion
}
