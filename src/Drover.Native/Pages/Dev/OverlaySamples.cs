namespace Drover.Native.Pages;

/// <summary>The gallery's samples of what opens over a screen: a dialog, a sheet, a menu, a tooltip, a toast.</summary>
public static class OverlaySamples
{
    public static IEnumerable<UIElement> Make(ToastService toasts)
    {
        var keep = new Button { Content = "Keep bot" };
        var remove = new Button { Content = "Remove bot", Kind = ButtonKind.Danger };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        actions.Children.Add(keep);
        actions.Children.Add(remove);
        var dialog = new Dialog
        {
            Title = "Remove sample bot",
            Body = Styles.Text("Its conversations stay. It stops answering."),
            Actions = actions,
        };
        keep.Click += (_, _) => dialog.Open = false;
        remove.Click += (_, _) => dialog.Open = false;

        var sheet = new Sheet
        {
            Title = "Sample bot",
            Body = Styles.Text("A sheet holds detail beside the page on a wide window and rises from the bottom on a phone."),
        };

        var openDialog = new Button { Content = "Open dialog" };
        openDialog.Click += (_, _) => dialog.Open = true;
        var openSheet = new Button { Content = "Open sheet" };
        openSheet.Click += (_, _) => sheet.Open = true;

        var menu = new Menu { Label = "Bot actions" };
        menu.Items.Add(Item("Pause", () => toasts.Show("Sample bot paused.", ToastKind.Success)));
        menu.Items.Add(Item("Duplicate", () => toasts.Show("Sample bot duplicated.", ToastKind.Success)));
        menu.Items.Add(new MenuItem { Content = "Remove", IsEnabled = false, HorizontalContentAlignment = HorizontalAlignment.Left });

        var tooltip = new Tooltip { Text = "Copies the sample address.", Described = new Button { Content = "Copy address" } };

        var error = new Button { Content = "Show an error toast" };
        error.Click += (_, _) => toasts.Show("The sample connection could not be reached. Check the address.", ToastKind.Error);

        return [openDialog, openSheet, menu, tooltip, error, dialog, sheet];
    }

    private static MenuItem Item(string label, Action choose)
    {
        var item = new MenuItem { Content = label, HorizontalContentAlignment = HorizontalAlignment.Left };
        item.Selected += (_, _) => choose();
        return item;
    }
}
