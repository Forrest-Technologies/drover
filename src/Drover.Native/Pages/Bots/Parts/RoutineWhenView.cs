namespace Drover.Native.Pages;

/// <summary>
/// The "When" part of writing a routine: one choice of rhythm, then only that rhythm's fields, and under them the
/// sentence the routine will be listed as. Nobody has to know cron to use it.
/// </summary>
public sealed class RoutineWhenView : StackPanel
{
    #region Private Fields

    private readonly RoutinesSectionViewModel part;

    private readonly Select rhythm;

    private readonly StackPanel fields = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly Field timeZone;

    private readonly TextBlock reads = BotParts.Hint();

    private readonly StatusNote problem = new() { Kind = NoteKind.Danger };

    private RoutineRhythm? builtFor;

    private bool showing;

    #endregion

    #region Constructors

    public RoutineWhenView(RoutinesSectionViewModel part)
    {
        this.part = part;
        Spacing = Tokens.Number("DvSpace3");
        rhythm = new Select { Label = RoutineWhenFields.WhenLabel }.OnChanged(() => showing, value => When(when => RoutineWhenFields.ChooseRhythm(when, value)));
        timeZone = new Field { Label = RoutineWhenFields.TimeZoneLabel, Hint = RoutineWhenFields.TimeZoneHint, Value = part.TimeZone }
            .OnChanged(() => showing, value => _ = part.ChangeTimeZone(value));
        AutomationProperties.SetLiveSetting(reads, AutomationLiveSetting.Polite);
        Children.Add(rhythm);
        Children.Add(fields);
        Children.Add(timeZone);
        Children.Add(reads);
        Children.Add(problem);
    }

    #endregion

    #region Public Methods

    public void Show()
    {
        showing = true;
        var when = part.When;
        rhythm.Show(RoutineWhenFields.Rhythms, when.Rhythm.ToString());

        // A rhythm's fields are made for it when it is chosen; while it stays, what is typed in them stays as typed.
        if (builtFor != when.Rhythm)
        {
            builtFor = when.Rhythm;
            fields.Fill(Fields(when));
        }

        fields.Shown(fields.Children.Count > 0);
        reads.Show(part.Reads);
        problem.Show(part.Refusal);
        showing = false;
    }

    #endregion

    #region Private Methods

    private List<UIElement> Fields(RoutineWhen when)
    {
        List<UIElement> shown = when.Rhythm switch
        {
            RoutineRhythm.ChosenDays => [Days(when)],
            RoutineRhythm.DayOfMonth =>
            [
                Text(RoutineWhenFields.DayOfMonthLabel, RoutineWhenFields.Number(when.DayOfMonth), RoutineWhenFields.DayOfMonthHint, RoutineWhenFields.SetDayOfMonth),
            ],
            RoutineRhythm.EveryHours =>
            [
                Hours(when),
                Text(RoutineWhenFields.MinuteLabel, RoutineWhenFields.Number(when.Time.Minute), null, RoutineWhenFields.SetMinute),
            ],
            RoutineRhythm.Custom => [Text(RoutineWhenFields.CronLabel, when.Cron, RoutineWhenFields.CronHint, RoutineWhenFields.SetCron)],
            _ => [],
        };

        if (RoutineWhenFields.AtShown(when))
        {
            shown.Add(Text(RoutineWhenFields.AtLabel, RoutineWhenFields.At(when), null, (current, text) => RoutineWhenFields.SetTime(current, text) ?? current));
        }

        return shown;
    }

    private NamedGroup Days(RoutineWhen when)
    {
        var days = new FlowPanel { Gap = Tokens.Number("DvSpace4") };
        foreach (var day in RoutineWords.Week)
        {
            var box = new CheckItem(day.ToString());
            box.Show(when.Days.Contains(day));
            box.Changed += chosen => When(current => RoutineWhenFields.ToggleDay(current, day, chosen));
            days.Children.Add(box);
        }

        return new NamedGroup(RoutineWhenFields.DaysLegend, "DvSpace1", BotParts.ChoiceLabel(RoutineWhenFields.DaysLegend), days);
    }

    private Select Hours(RoutineWhen when)
    {
        var hours = new Select { Label = RoutineWhenFields.HoursLabel };
        hours.Show(RoutineWhenFields.HoursOptions, RoutineWhenFields.Number(when.Hours));
        return hours.OnChanged(() => showing, value => When(current => RoutineWhenFields.SetHours(current, value)));
    }

    private Field Text(string label, string value, string? hint, Func<RoutineWhen, string, RoutineWhen> change) =>
        new Field { Label = label, Hint = hint, Value = value }.OnChanged(() => showing, text => When(current => change(current, text)));

    private void When(Func<RoutineWhen, RoutineWhen> change)
    {
        var changed = change(part.When);
        if (changed != part.When)
        {
            _ = part.ChangeWhen(changed);
        }
    }

    #endregion
}
