namespace Drover.Native.Pages;

/// <summary>
/// Where a person's sign-in to a model provider stands, drawn from its <see cref="ModelSignInFlow"/>: the state's one
/// sentence, the code to enter on the provider's page with a button that copies it, the field a pasted code goes in,
/// the provider's page as a link, and the state's one main action, with Cancel beside it while a code is pasted. The
/// connect step, "Add a connection" and a connection's card all draw it the same way. Focus moves to the sentence when
/// the state changes. At phone width the code and its Copy button stay on one row and the buttons stack.
/// </summary>
internal sealed class ModelSignInPanel : StackPanel
{
    #region Private Fields

    private readonly ModelSignInFlow flow;

    /// <summary>The kind the host gives the action that starts or sends: the host's form or sheet holds its one primary action.</summary>
    private readonly ButtonKind startKind;

    private readonly ContentControl noteHolder = new() { IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };

    private readonly StatusNote note = new();

    private readonly Grid codeRow = new() { ColumnSpacing = Tokens.Number("DvSpace2") };

    private readonly TextBlock code = Styles.Text(string.Empty, "DvTextH3");

    private readonly CopyButton copy = new() { Label = ModelSignInFlow.CopyWords, VerticalAlignment = VerticalAlignment.Center };

    private readonly Link page = new() { OpensNewTab = true };

    private readonly Field pasted = new() { Label = ModelSignInFlow.PasteLabel, Autofill = "one-time-code" };

    private readonly Button action = new();

    private readonly Button cancel = new() { Kind = ButtonKind.Quiet, Content = ModelSignInFlow.CancelWords };

    #endregion

    #region Constructors

    /// <param name="flow">The sign-in it draws.</param>
    /// <param name="act">What the host does for the state's main action: start, send a pasted code, cancel, or add a key.</param>
    /// <param name="stretch">True to fill the width with the buttons, as the connect step does; false to keep them at their own width.</param>
    /// <param name="startKind">The kind of the button that starts the sign-in or sends a pasted code; the rest are quiet.</param>
    public ModelSignInPanel(ModelSignInFlow flow, Func<Task> act, bool stretch, ButtonKind startKind)
    {
        this.flow = flow;
        this.startKind = startKind;
        Spacing = Tokens.Number("DvSpace3");
        noteHolder.Content = note;
        noteHolder.LostFocus += (_, _) => noteHolder.IsTabStop = false;

        code.FontFamily = Tokens.FontMono;
        code.IsTextSelectionEnabled = true;
        code.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(code, "Sign-in code");
        codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(copy, 1);
        codeRow.Children.Add(code);
        codeRow.Children.Add(copy);
        copy.Text = () => flow.Code ?? string.Empty;

        var alignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        action.HorizontalAlignment = cancel.HorizontalAlignment = alignment;
        AutomationProperties.SetName(cancel, ModelSignInFlow.CancelWords);
        action.Click += async (_, _) => await act();
        cancel.Click += (_, _) => flow.Cancel();
        pasted.RegisterPropertyChangedCallback(Field.ValueProperty, (_, _) =>
        {
            if (pasted.Value != flow.PastedCode)
            {
                flow.PastedCode = pasted.Value;
            }
        });
        pasted.Submitted += async (_, _) => await act();

        Children.Add(noteHolder);
        Children.Add(codeRow);
        Children.Add(page);
        Children.Add(pasted);
        Children.Add(action);
        Children.Add(cancel);
        Refresh(shown: true, takeFocus: false);
    }

    #endregion

    #region Public Methods

    /// <summary>Opens approval while the app keeps showing the sign-in.</summary>
    public static Task OpenApprovalPage(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new DeviceCallFailedException("The sign-in address must use HTTPS.");
        }

        return Link.OpenOutside(uri, opensNewTab: true);
    }

    /// <summary>Draws the flow as it stands. <paramref name="takeFocus"/> lets the sentence take focus when it changed.</summary>
    public void Refresh(bool shown, bool takeFocus)
    {
        Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        var changed = flow.Sentence is { Length: > 0 } && note.Text != flow.Sentence;
        note.Text = flow.Sentence ?? string.Empty;
        note.Kind = flow.Stage switch
        {
            ModelSignInStage.Done => NoteKind.Ok,
            ModelSignInStage.Waiting or ModelSignInStage.Code or ModelSignInStage.Paste => NoteKind.Info,
            _ => NoteKind.Danger,
        };
        noteHolder.Visibility = flow.Sentence is null ? Visibility.Collapsed : Visibility.Visible;

        code.Text = flow.Code ?? string.Empty;
        codeRow.Visibility = flow.Code is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        page.Text = flow.PageWords ?? string.Empty;
        page.Address = flow.Page;
        AutomationProperties.SetName(page, flow.PageWords ?? string.Empty);
        page.Visibility = flow.Page is not null && flow.PageWords is not null ? Visibility.Visible : Visibility.Collapsed;

        pasted.Visibility = flow.ShowsPaste ? Visibility.Visible : Visibility.Collapsed;
        if (pasted.Value != flow.PastedCode)
        {
            pasted.Value = flow.PastedCode;
        }

        action.Content = flow.ActionWords;
        AutomationProperties.SetName(action, flow.ActionWords ?? string.Empty);
        action.Kind = flow.Action is ModelSignInAction.Continue or ModelSignInAction.TryAgain or ModelSignInAction.GetNewCode ? startKind : ButtonKind.Quiet;
        action.Visibility = flow.ActionWords is null ? Visibility.Collapsed : Visibility.Visible;
        cancel.Visibility = flow.OffersCancel ? Visibility.Visible : Visibility.Collapsed;

        // Focus moves to the sentence when the state changes, so a keyboard or screen reader lands on it; the field
        // and the buttons follow it in tab order.
        if (changed && takeFocus && shown)
        {
            noteHolder.IsTabStop = true;
            noteHolder.Focus(FocusState.Programmatic);
        }
    }

    #endregion
}
