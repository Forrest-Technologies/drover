namespace Drover.Native.Chat;

/// <summary>The allowed models for a conversation, with the current choice marked.</summary>
internal sealed class ConversationModelPicker
{
    #region Private Fields

    private readonly Flyout flyout = new() { Placement = FlyoutPlacementMode.TopEdgeAlignedLeft };

    private readonly Field search = new() { Label = "Search models" };

    private readonly StackPanel choices = new();

    private readonly StackPanel body = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly ScrollViewer scroll;

    private ListWindow<ModelRow> window = new(row => row.Key);

    private ComposerViewModel? model;

    #endregion

    #region Constructors

    public ConversationModelPicker()
    {
        AutomationProperties.SetName(body, ComposerViewModel.ModelLabel);
        var title = Pieces.PartTitle(ComposerViewModel.ModelLabel);
        scroll = new()
        {
            Content = choices,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
        };

        body.Children.Add(title);
        body.Children.Add(search);
        body.Children.Add(scroll);
        search.RegisterPropertyChangedCallback(Field.ValueProperty, (_, _) => Filter());
        flyout.Content = body;
        flyout.Closed += (_, _) => model = null;
    }

    #endregion

    #region Public Methods

    public void Open(Control anchor, ComposerViewModel composer)
    {
        if (!composer.HasModelChoice || composer.Disabled || composer.ChoosingModel || anchor.XamlRoot is not { } root)
        {
            return;
        }

        model = composer;
        var pad = Tokens.Number("DvSpace2");
        var border = Tokens.Number("DvBorder");
        body.Width = Math.Max(0, Math.Min(Tokens.Number("DvToastWidth"), root.Size.Width - (2 * (pad + border))));
        scroll.MaxHeight = Math.Min(Tokens.Number("DvMeasure") / 2, root.Size.Height / 2);
        body.RequestedTheme = anchor.ActualTheme;

        var presenter = new Style(typeof(FlyoutPresenter));
        presenter.Setters.Add(new Setter(Control.BackgroundProperty, Tokens.Brush(anchor, "DvBg2")));
        presenter.Setters.Add(new Setter(Control.BorderBrushProperty, Tokens.Brush(anchor, "DvLine")));
        presenter.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(border)));
        presenter.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(Tokens.Number("DvRadiusCard"))));
        presenter.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(pad)));
        presenter.Setters.Add(new Setter(Control.IsTabStopProperty, false));
        flyout.FlyoutPresenterStyle = presenter;

        search.Value = string.Empty;
        Filter();
        flyout.ShowAt(anchor);
    }

    public void Close() => flyout.Hide();

    #endregion

    #region Private Methods

    private void Filter()
    {
        if (model is not { } composer)
        {
            return;
        }

        var query = search.Value.Trim();
        var fallback = composer.ModelConnectionName ?? "Models on this connection";
        var options = composer.ModelOptions.Where(option => option.Value != ComposerViewModel.BotsOwnModel)
            .Select(option => (Option: option, Provider: option.Provider ?? fallback))
            .Where(row => row.Option.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.Option.Value.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.Provider.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        List<ModelRow> rows = [];
        if (query.Length == 0)
        {
            var byId = options.ToDictionary(row => row.Option.Value, row => row.Option);
            foreach (var id in composer.RecentModels?.Invoke() ?? [])
            {
                if (byId.TryGetValue(id, out var recent))
                {
                    rows.Add(new($"recent:{id}", "Recent models", recent));
                }
            }
        }

        rows.AddRange(options.OrderBy(row => row.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Option.Label, StringComparer.OrdinalIgnoreCase)
            .Select(row => new ModelRow($"model:{row.Option.Value}", row.Provider, row.Option)));

        // The window opens at the end of its input. Reverse it so the picker starts with the first search results.
        rows.Reverse();
        window = new(row => row.Key) { Step = 30, Most = 30 };
        window.Show(rows);
        Draw();
    }

    private void Draw(bool moveFocus = false)
    {
        if (model is not { } composer)
        {
            return;
        }

        choices.Children.Clear();
        var defaultRow = AddChoice(composer, composer.ModelOptions[0]);
        if (window.Later > 0)
        {
            AddPage("Previous models", window.ShowLater);
        }

        string? heading = null;
        PlainButton? first = null;
        foreach (var row in window.Shown.Reverse())
        {
            if (row.Group != heading)
            {
                heading = row.Group;
                var label = Pieces.PartTitle(heading);
                label.Margin = new(Tokens.Number("DvSpace3"), Tokens.Number("DvSpace2"), 0, 0);
                choices.Children.Add(label);
            }

            var option = AddChoice(composer, row.Option);
            first ??= option;
        }

        if (window.Earlier > 0)
        {
            AddPage("More models", window.ShowEarlier);
        }

        if (window.Shown.Count == 0)
        {
            choices.Children.Add(Styles.Text("No matching models. Try a different search.", "DvTextSecondary"));
        }

        scroll.ChangeView(null, 0, null, disableAnimation: true);
        if (moveFocus)
        {
            Pieces.FocusWhenShown(first ?? defaultRow, (first ?? defaultRow).TakeFocus);
        }
    }

    private PlainButton AddChoice(ComposerViewModel composer, ModelOption option)
    {
        var selected = composer.ChosenModel == option.Value;
        var words = new TextBlock { Text = option.Label, TextWrapping = TextWrapping.Wrap };
        var row = new Grid { ColumnSpacing = Tokens.Number("DvSpace2") };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = new(Tokens.Number("DvIcon16")) });
        row.Children.Add(words);

        if (selected)
        {
            var check = new Icon { NameOfIcon = IconName.Check, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(check, 1);
            row.Children.Add(check);
        }

        var button = RowButton(row);
        button.Fill = selected ? "DvBg3" : null;
        AutomationProperties.SetName(button, option.Label);
        AutomationProperties.SetItemStatus(button, selected ? "Active model" : string.Empty);
        button.Click += async (_, _) =>
        {
            flyout.Hide();
            await composer.ChooseModel(option.Value);
        };
        choices.Children.Add(button);
        return button;
    }

    private void AddPage(string label, Func<bool> turn)
    {
        var button = RowButton(label);
        button.Click += (_, _) =>
        {
            if (turn())
            {
                Draw(moveFocus: true);
            }
        };
        choices.Children.Add(button);
    }

    private static PlainButton RowButton(object content) => new()
    {
        Content = content,
        MinHeight = Targets.Minimum,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Padding = Tokens.Space("DvSpace3", "DvSpace1"),
        RingInside = true,
    };

    #endregion

    #region Nested Types

    private sealed record ModelRow(string Key, string Group, ModelOption Option);

    #endregion
}
