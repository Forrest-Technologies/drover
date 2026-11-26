namespace Drover.Native.Pages;

/// <summary>
/// One row of the Voice page: the picker of the model that fills a slot, what the slot uses now, and where the slot
/// speaks, the picker of its voice with the button that plays a sample of it.
/// </summary>
internal sealed class VoiceRowView : Grid
{
    #region Private Fields

    private readonly Select models = new();

    private readonly TextBlock uses = Styles.Text(null, "DvTextSecondary");

    private readonly Select voices = new() { Label = VoiceWords.VoiceLabel };

    private readonly IconButton sample = new() { Icon = IconName.Play, Label = VoiceWords.PlaySample, VerticalAlignment = VerticalAlignment.Bottom };

    private readonly Grid voiceRow = new() { ColumnSpacing = Tokens.Number("DvSpace2") };

    // True while the row is being told what to show, so that is not taken for a person's choice.
    private bool telling;

    #endregion

    #region Constructors

    public VoiceRowView(VoiceSettingsViewModel model, VoiceSlot slot)
    {
        voiceRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        voiceRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        SetColumn(sample, 1);
        voiceRow.Children.Add(voices);
        voiceRow.Children.Add(sample);
        uses.TextWrapping = TextWrapping.Wrap;
        Children.Add(PageParts.Card(models, uses, voiceRow));

        AutomationProperties.SetAutomationId(models, $"Voice{slot}Model");
        AutomationProperties.SetAutomationId(voices, $"Voice{slot}Voice");
        AutomationProperties.SetAutomationId(sample, $"Voice{slot}Sample");
        models.RegisterPropertyChangedCallback(Select.ValueProperty, async (_, _) =>
        {
            if (!telling)
            {
                await model.ChooseModel(slot, models.Value);
            }
        });
        voices.RegisterPropertyChangedCallback(Select.ValueProperty, async (_, _) =>
        {
            if (!telling)
            {
                await model.ChooseVoice(slot, voices.Value);
            }
        });
        sample.Click += async (_, _) => await model.PlaySample();
    }

    #endregion

    #region Public Methods

    public void Show(VoiceRow row, VoiceSettingsViewModel model)
    {
        telling = true;
        models.Label = row.Title;
        Tell(models, row.Models.Select(option => new SelectOption(option.Value, option.Group is null ? option.Label : $"{option.Group}: {option.Label}")));
        models.Value = row.Model;
        models.Disabled = !model.CanEdit;
        uses.Text = row.Uses;

        Tell(voices, row.Voices.Select(option => new SelectOption(option.Value, option.Label)));
        voices.Value = row.Voice;
        voices.Disabled = !row.CanChooseVoice;
        voices.Visibility = row.Voices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        sample.Visibility = row.CanSample ? Visibility.Visible : Visibility.Collapsed;
        sample.Icon = model.SamplePlaying ? IconName.Stop : IconName.Play;
        sample.Label = model.SampleLabel;
        voiceRow.Visibility = row.Voices.Count > 0 || row.CanSample ? Visibility.Visible : Visibility.Collapsed;
        telling = false;
    }

    #endregion

    #region Private Methods

    // The list is given again only when it changed, so an open picker is not closed under the person.
    private static void Tell(Select picker, IEnumerable<SelectOption> options)
    {
        List<SelectOption> wanted = [.. options];

        if (!picker.Options.SequenceEqual(wanted))
        {
            picker.Options = wanted;
        }
    }

    #endregion
}
