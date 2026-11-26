namespace Drover.Native.Session;

/// <summary>
/// Notifications on this device, as the account menu shows them: the state the device and the server report, the
/// one action when there is one, and the next step where a state's next step is a setting of the system. Where the
/// platform has nothing to register with, it says so in a sentence and offers nothing.
/// </summary>
public sealed class DevicePush : StackPanel
{
    #region Private Fields

    private readonly DevicePushViewModel model =
        ActivatorUtilities.CreateInstance<DevicePushViewModel>(((App)Application.Current).Services);

    private bool started;

    #endregion

    #region Constructors

    public DevicePush()
    {
        Spacing = Tokens.Number("DvSpace2");
        model.PropertyChanged += (_, _) => Show();
        Loaded += async (_, _) => await Start();
        Show();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty TitleHiddenProperty = Dp.Register<DevicePush, bool>(nameof(TitleHidden), false, push => push.Show());

    /// <summary>True where a heading around the control already names it, such as a dialog's title.</summary>
    public bool TitleHidden
    {
        get => (bool)GetValue(TitleHiddenProperty);
        set => SetValue(TitleHiddenProperty, value);
    }

    #endregion

    #region Private Methods

    // The device is asked once, when the control first appears: not at launch, and not each time it is rebuilt.
    private async Task Start()
    {
        if (!started && model.Available)
        {
            started = true;
            await model.Check();
        }
    }

    private void Show()
    {
        Children.Clear();

        if (!model.Available)
        {
            Children.Add(new StatusNote { Text = model.StatusSentence });
        }
        else if (model.State is { } state)
        {
            AddKnown(state);
        }
        else if (model.Problem is not null)
        {
            Children.Add(new StatusNote { Kind = NoteKind.Danger, Text = model.Problem });
            Children.Add(CheckAgain());
        }
        else
        {
            var checking = Styles.Text(DevicePushViewModel.CheckingNote, "DvTextSecondary");
            AutomationProperties.SetLiveSetting(checking, AutomationLiveSetting.Polite);
            Children.Add(checking);
        }
    }

    private void AddKnown(PushState state)
    {
        var control = new PushControl { State = state, Working = model.Working, Problem = model.Problem, TitleHidden = TitleHidden };
        control.Toggle += (_, _) => _ = model.ToggleCommand.ExecuteAsync(null);
        Children.Add(control);

        if (model.Settings is { } settings)
        {
            AddSettingsSteps(state, settings);
        }

        if (model.ShowsStatusLink)
        {
            var status = new FlexPanel { Gap = 0 };
            status.Children.Add(new Link { Text = "Open the status page", Address = Addresses.Status });
            status.Children.Add(Styles.Text(" to see what the server needs.", "DvTextSecondary"));
            Children.Add(status);
        }
    }

    private void AddSettingsSteps(PushState state, IDeviceNotificationSettings settings)
    {
        switch (state)
        {
            case PushState.DeviceBlocked:
                Children.Add(Quiet("Open notification settings", settings.OpenNotificationSettings));
                Children.Add(CheckAgain());
                break;
            case PushState.NeedsDistributor:
                Children.Add(new Link { Text = "Find a UnifiedPush distributor", Address = "https://unifiedpush.org/users/distributors/" });
                Children.Add(CheckAgain());
                break;
            case PushState.On:
                Children.Add(LockScreenPreview(settings));
                Children.Add(Quiet("Send a test notification", () => _ = model.SendTestCommand.ExecuteAsync(null), enabled: !model.Working));
                Children.Add(new StatusNote { Kind = NoteKind.Ok, Text = model.TestNotice });
                Children.Add(LateNotice(settings));
                break;
        }
    }

    private static Switch LockScreenPreview(IDeviceNotificationSettings settings)
    {
        var preview = new Switch
        {
            Label = "Show message text on the lock screen",
            Value = settings.ShowMessageTextOnLockScreen,
        };
        preview.Checked += (_, _) => settings.ShowMessageTextOnLockScreen = true;
        preview.Unchecked += (_, _) => settings.ShowMessageTextOnLockScreen = false;
        return preview;
    }

    private Disclosure LateNotice(IDeviceNotificationSettings settings)
    {
        var body = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        body.Children.Add(Styles.Text("Check whether Android restricts your notification distributor in the background.", "DvTextSecondary"));
        body.Children.Add(Quiet("Open battery settings", () => _ = settings.OpenBatterySettings()));
        return new Disclosure { Title = "If notifications arrive late", Body = body };
    }

    private Button CheckAgain() => Quiet("Check again", () => _ = model.CheckCommand.ExecuteAsync(null), enabled: !model.Working);

    private static Button Quiet(string label, Action click, bool enabled = true)
    {
        var button = new Button { Kind = ButtonKind.Quiet, Content = label, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => click();
        return button;
    }

    #endregion
}
