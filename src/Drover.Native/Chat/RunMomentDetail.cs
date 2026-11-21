namespace Drover.Native.Chat;

/// <summary>
/// What one node of a run's timeline holds, opened under it: what a step wrote, a call's arguments, its output as it
/// streams, its result, a wait's answer, the task handed over and what came back. Everything a model or a tool wrote
/// is untrusted text: words go through the markdown view, which draws only the nodes it knows, and machine text is
/// shown as text in the machine typeface. A sandbox command reads as a terminal: the command, then what it printed.
/// </summary>
public sealed class RunMomentDetail : StackPanel
{
    #region Private Fields

    private readonly RunMomentDetailViewModel model = new();

    private readonly Batch batch;

    // Kept while the node is drawn again, so what a person scrolled to stays put while output arrives.
    private readonly OutputBox output = new() { Fill = "DvBg0", Rounded = false, FollowsEnd = true };

    private readonly MarkdownView words = new();

    private readonly TextBlock question = Styles.Text(null);

    private readonly Button approve = new() { Kind = ButtonKind.Primary, Content = RunMomentDetailViewModel.Approve };

    private readonly Button deny = new() { Content = RunMomentDetailViewModel.Deny };

    private readonly FlowPanel answers = new() { Gap = Tokens.Number("DvSpace2") };

    private readonly StatusNote problem = new() { Kind = NoteKind.Danger };

    #endregion

    #region Constructors

    public RunMomentDetail()
    {
        batch = new(this, Apply, Draw);
        Spacing = Tokens.Number("DvSpace3");
        approve.Click += async (_, _) => await model.Answer(true);
        deny.Click += async (_, _) => await model.Answer(false);
        answers.Children.Add(approve);
        answers.Children.Add(deny);
        model.PropertyChanged += (_, _) => batch.Draw();
        this.WhenShownOrRethemed(batch.Draw);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty TreeProperty = Dp.Register<RunMomentDetail, RunTreeView?>(nameof(Tree), null, detail => detail.batch.Values());

    public static readonly DependencyProperty MomentProperty = Dp.Register<RunMomentDetail, RunMoment?>(nameof(Moment), null, detail => detail.batch.Values());

    public static readonly DependencyProperty BotNameProperty = Dp.Register<RunMomentDetail, string>(nameof(BotName), string.Empty, detail => detail.batch.Values());

    public static readonly DependencyProperty ChildNameProperty =
        Dp.Register<RunMomentDetail, string>(nameof(ChildName), string.Empty, detail => detail.batch.Values());

    public static readonly DependencyProperty DecideProperty =
        Dp.Register<RunMomentDetail, Func<Guid, bool, Task<string?>>?>(nameof(Decide), null, detail => detail.batch.Values());

    public static readonly DependencyProperty ResultsProperty =
        Dp.Register<RunMomentDetail, IReadOnlyDictionary<Guid, PartView>?>(nameof(Results), null, detail => detail.batch.Values());

    /// <summary>The run the node belongs to, with every run beneath it. Required.</summary>
    public RunTreeView? Tree
    {
        get => (RunTreeView?)GetValue(TreeProperty);
        set => SetValue(TreeProperty, value);
    }

    /// <summary>The node. Required.</summary>
    public RunMoment? Moment
    {
        get => (RunMoment?)GetValue(MomentProperty);
        set => SetValue(MomentProperty, value);
    }

    /// <summary>The name of the bot whose node this is. Required.</summary>
    public string BotName
    {
        get => (string)GetValue(BotNameProperty);
        set => SetValue(BotNameProperty, value);
    }

    /// <summary>The name of the bot work went to, for a hand-off and its answer.</summary>
    public string ChildName
    {
        get => (string)GetValue(ChildNameProperty);
        set => SetValue(ChildNameProperty, value);
    }

    /// <summary>Sends a person's answer to a call that waits for one. Returns why it failed, or nothing when it was sent.</summary>
    public Func<Guid, bool, Task<string?>>? Decide
    {
        get => (Func<Guid, bool, Task<string?>>?)GetValue(DecideProperty);
        set => SetValue(DecideProperty, value);
    }

    /// <summary>Stored results of calls, where the conversation has them.</summary>
    public IReadOnlyDictionary<Guid, PartView>? Results
    {
        get => (IReadOnlyDictionary<Guid, PartView>?)GetValue(ResultsProperty);
        set => SetValue(ResultsProperty, value);
    }

    #endregion

    #region Drawing

    private void Apply()
    {
        if (Tree is not { } tree || Moment is not { } moment)
        {
            return;
        }

        (model.Tree, model.Moment, model.BotName, model.ChildName, model.Decide, model.Results) = (tree, moment, BotName, ChildName, Decide, Results);
        model.ParametersSet();
    }

    private void Draw()
    {
        if (Tree is null || Moment is null || model.Tree is null || model.Moment is null)
        {
            return;
        }

        List<UIElement> next = [];
        if (model.Facts.Count > 0)
        {
            next.Add(Facts());
        }

        switch (model.Kind)
        {
            case RunMomentKind.Step:
                DrawStep(next);
                break;
            case RunMomentKind.Tool when model.Card is { } card:
                DrawTool(next, card);
                break;
            case RunMomentKind.Approval:
                DrawApproval(next);
                break;
            case RunMomentKind.Delegation:
                next.Add(model.HandedTask is { Length: > 0 } task
                    ? Pieces.Part(model.DelegationTitle, Said(task))
                    : Pieces.Part(model.DelegationTitle));
                break;
            case RunMomentKind.Answer:
                next.Add(Pieces.Part(model.AnswerTitle, model.AnswerText is { } answer ? Said(answer) : Quiet(model.AnswerQuiet)));
                break;
            case RunMomentKind.Rule:
                next.Add(Styles.Text(model.RuleSentence));
                if (model.RuleWhy is { } why)
                {
                    next.Add(Quiet(why));
                }

                break;
        }

        ShowOnly(next);
    }

    // Direct children that keep state or focus (such as answers) stay in Children when still shown.
    private void ShowOnly(List<UIElement> wanted)
    {
        for (var index = Children.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(Children[index]))
            {
                Children.RemoveAt(index);
            }
        }

        for (var index = 0; index < wanted.Count; index++)
        {
            if (index < Children.Count && ReferenceEquals(Children[index], wanted[index]))
            {
                continue;
            }

            Children.Remove(wanted[index]);
            Children.Insert(index, wanted[index]);
        }
    }

    // How long it took and what it cost, each a name and its value, side by side.
    private FlowPanel Facts()
    {
        var facts = new FlowPanel { Gap = Tokens.Number("DvSpace4") };
        foreach (var (name, value) in model.Facts)
        {
            var label = Styles.Text(name);
            label.Foreground = Tokens.Brush(this, "DvText3");
            var fact = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
            fact.Children.Add(label);
            fact.Children.Add(Styles.Text(value));
            facts.Children.Add(fact);
        }

        return facts;
    }

    private void DrawStep(List<UIElement> next)
    {
        if (model.StepText is { } text)
        {
            next.Add(Said(text, model.StepStreaming));
        }
        else
        {
            next.Add(Quiet(model.StepQuiet));
        }
    }

    private void DrawTool(List<UIElement> next, ToolCard card)
    {
        if (model.IsCommand)
        {
            output.Label = RunMomentDetailViewModel.TerminalName;
            output.Show(RunMomentDetailViewModel.Prompt, model.CommandLine, card.Output);
            next.Add(Pieces.Part(RunMomentDetailViewModel.CommandTitle, Pieces.Detached(output)));
        }
        else
        {
            if (model.ShowsArguments)
            {
                next.Add(Pieces.Part(RunMomentDetailViewModel.ArgumentsTitle, new CodeBlock { Code = card.Arguments }));
            }

            if (model.ShowsOutput)
            {
                output.Label = RunMomentDetailViewModel.OutputTitle;
                output.Show(null, null, card.Output);
                next.Add(Pieces.Part(RunMomentDetailViewModel.OutputTitle, Pieces.Detached(output)));
            }
        }

        if (model.ShowsResult)
        {
            var result = Pieces.Part(RunMomentDetailViewModel.ResultTitle);
            if (model.ExitLine is { } exit)
            {
                result.Children.Add(Quiet(exit));
            }

            if (card.Result is { } text)
            {
                result.Children.Add(new CodeBlock { Code = text, CodeLanguage = card.ResultLanguage });
            }

            if (model.ResultIsCut)
            {
                result.Children.Add(Quiet(RunMomentDetailViewModel.CutNote));
            }

            next.Add(result);
        }

        if (model.ShowsQuiet)
        {
            next.Add(Quiet(RunMomentDetailViewModel.Quiet));
        }
    }

    private void DrawApproval(List<UIElement> next)
    {
        question.Text = model.Asks ? model.Question : model.Answered;
        next.Add(question);

        if (model.Card is { } asked)
        {
            if (asked.Facts.Count > 0)
            {
                next.Add(Pieces.Facts(asked.Facts));
            }
            else if (model.ApprovalShowsArguments)
            {
                next.Add(new CodeBlock { Code = asked.Arguments, CodeLanguage = RunMomentDetailViewModel.ArgumentsTitle });
            }
        }

        if (!model.Asks)
        {
            return;
        }

        // Each answer is described by the question, so it is never just "Approve" out of context.
        AutomationProperties.SetFullDescription(approve, model.Question);
        AutomationProperties.SetFullDescription(deny, model.Question);
        approve.IsEnabled = deny.IsEnabled = !model.Deciding;
        problem.Text = model.Problem;
        next.Add(answers);
        next.Add(problem);
    }

    // The one view is kept, so text still arriving fades in where it lands instead of the whole reply blinking.
    private MarkdownView Said(string text, bool streaming = false)
    {
        (words.Text, words.Streaming) = (text, streaming);
        return Pieces.Detached(words);
    }

    private static TextBlock Quiet(string text) => Styles.Text(text, "DvTextSecondary");

    #endregion
}
