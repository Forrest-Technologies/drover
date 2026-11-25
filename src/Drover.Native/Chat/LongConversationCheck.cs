namespace Drover.Native.Chat;

/// <summary>
/// Proves that a long conversation costs what its page holds, and that nothing of it is out of reach: the newest
/// messages are shown first, going back shows what came before without moving what is being read, a link lands on a
/// message far from the page, and sending comes back to the newest. It then says what the list cost: the time to
/// first show, one update of a streaming reply, and a frame while scrolling. Only compiled when
/// <c>DroverLongConversationCheck=true</c> and started with <c>DROVER_LONG_CONVERSATION_CHECK=1</c>;
/// <c>DROVER_LONG_CONVERSATION_COUNT</c> sets how many sample messages there are.
/// </summary>
internal static class LongConversationCheck
{
    #region Private Fields

    private const int Messages = 2000;

    private const int Updates = 40;

    private const int Frames = 120;

    // The most messages made to open a conversation: the window's first page, and one step if that did not fill a view.
    private const int FirstPage = 40;

    // The most messages a page may hold: the window's own limit, and room for the turn it ends in.
    private const int MostOnPage = 210;

    private static readonly ILogger logger = Log.Factory.CreateLogger(nameof(LongConversationCheck));

    private static string stage = "startup";

    // Where the list stands, for the line that says a check failed.
    private static Func<string> standing = () => string.Empty;

    #endregion

    #region Public Methods

    /// <summary>Shows a sample conversation in place of the first screen and runs the checks on it.</summary>
    public static void Start(Window window)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(3));
            Fail($"The long conversation check did not finish within 3 minutes (stage: {Volatile.Read(ref stage)}).");
        });

        var asked = Environment.GetEnvironmentVariable("DROVER_LONG_CONVERSATION_COUNT");
        var count = int.TryParse(asked, System.Globalization.CultureInfo.InvariantCulture, out var wanted) ? wanted : Messages;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var model = new MessageListViewModel { BotName = "Sample lead bot", Messages = Sample(count) };
        model.Update();
        var list = new MessageList { Model = model };
        var pane = new Grid { Width = Tokens.Number("DvMeasure") };
        pane.Children.Add(list);
        list.Loaded += (_, _) => _ = Run(list, model, clock);
        window.Content = pane;
    }

    #endregion

    #region Private Methods

    private static async Task Run(MessageList list, MessageListViewModel model, System.Diagnostics.Stopwatch clock)
    {
        try
        {
            var scroller = All<ScrollViewer>(list)[0];
            var total = model.Messages.Count;
            standing = () =>
            {
                var invariant = System.Globalization.CultureInfo.InvariantCulture;
                var page = string.Create(invariant, $"{list.Count} on the page, {model.Earlier} before, {model.Later} after");
                var view = string.Create(invariant, $"{scroller.VerticalOffset:F0} of {scroller.ScrollableHeight:F0}");
                return string.Create(invariant, $"{page}, at {view}, view {scroller.ViewportHeight:F0}");
            };

            var newest = model.Messages[^1].Key;
            await Until("the newest message at the foot", () => AtFoot(scroller) && Entry(list, newest) is { ActualHeight: > 0 });
            var first = clock.Elapsed.TotalMilliseconds;
            var opened = list.Count;
            Expect(opened <= FirstPage, $"{opened} of {total} messages were made to first show the conversation.");

            var update = await Stream(list, model, scroller);
            var scroll = await ScrollBack(list, model, scroller);
            await GoFarBack(list, model, scroller);
            await LandOnAMoment(list, model, scroller);
            await ComeBackToNewest(list, model, scroller);
            await StepBackByKeyboard(list, model);

            logger.LogInformation(
                "Long conversation: {Total} messages, {Opened} on the page at first, first show {First:F0} ms, "
                    + "update median {Update:F1} ms, scroll frame median {Scroll:F1} ms.",
                total,
                opened,
                first,
                update,
                scroll);
            logger.LogInformation("Long conversation check passed.");
            Exit(0);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The long conversation check failed (stage: {Stage}).", Volatile.Read(ref stage));
            Exit(1);
        }
    }

    // A reply arrives in stretches of text. The reader at the foot stays there, and nothing more is put on the page.
    private static async Task<double> Stream(MessageList list, MessageListViewModel model, ScrollViewer scroller)
    {
        Volatile.Write(ref stage, "streaming");
        var before = list.Count;
        List<double> took = [];

        for (var round = 0; round < Updates; round++)
        {
            // A conversation is worked out anew for each change: every message is another object that says the same.
            List<ChatMessage> next = [.. model.Messages.Select(message => message with { })];
            next[^1] = next[^1] with { Text = $"{next[^1].Text} A sample stretch of text." };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            model.Messages = next;
            model.Update();
            list.DrawNow();
            list.UpdateLayout();
            took.Add(watch.Elapsed.TotalMilliseconds);
            await Settle(list);
        }

        await Until("the foot kept while a reply streams", () => AtFoot(scroller));
        Expect(list.Count == before, $"A streaming reply changed the messages on the page from {before} to {list.Count}.");
        took.Sort();
        return took[took.Count / 2];
    }

    // The reader scrolls back, a tenth of a view at a time. Messages that came before are shown ahead of them, and the one
    // they are reading does not move when those arrive.
    private static async Task<double> ScrollBack(MessageList list, MessageListViewModel model, ScrollViewer scroller)
    {
        Volatile.Write(ref stage, "scrolling back");
        var earlier = model.Earlier;
        List<double> frames = [];
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stride = scroller.ViewportHeight / 10;

        // Each frame the screen draws moves the reader a tenth of a view back, and the time since the last is kept.
        void Frame(object? sender, object args)
        {
            frames.Add(watch.Elapsed.TotalMilliseconds);
            watch.Restart();
            scroller.ChangeView(null, Math.Max(0, scroller.VerticalOffset - stride), null, disableAnimation: true);

            if (frames.Count >= Frames)
            {
                done.TrySetResult();
            }
        }

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += Frame;
        await done.Task;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= Frame;
        await Until("earlier messages while scrolling back", () => earlier == 0 || model.Earlier < earlier);

        // Near the top of the page, with more before it: what arrives above must leave the reader's message in place.
        await Drawn(list, model);
        await Until("the page to rest", () => scroller.VerticalOffset >= scroller.ViewportHeight || model.Earlier == 0);

        if (model.Earlier == 0)
        {
            frames.Sort();
            return frames[frames.Count / 2];
        }

        earlier = model.Earlier;
        scroller.ChangeView(null, scroller.ViewportHeight / 2, null, disableAnimation: true);
        var reading = FirstInView(list, scroller);
        Expect(reading is not null, "No message was in view after scrolling back.");
        var top = ViewTop(reading!, scroller);
        await Until("earlier messages near the top of the page", () => model.Earlier < earlier);
        await Drawn(list, model);
        var moved = Math.Abs(ViewTop(reading!, scroller) - top);
        Expect(moved <= 1, $"The message being read moved {moved:F1} when earlier messages were shown above it.");
        Expect(reading!.Parent is not null, "The message being read was taken off the page.");

        frames.Sort();
        return frames[frames.Count / 2];
    }

    // Going back through hundreds of messages never holds more than the page keeps: the far end lets go.
    private static async Task GoFarBack(MessageList list, MessageListViewModel model, ScrollViewer scroller)
    {
        Volatile.Write(ref stage, "going far back");

        if (model.Messages.Count <= MostOnPage * 2)
        {
            return;
        }

        var target = model.Messages.Count - (MostOnPage * 2);
        for (var round = 0; round < 400 && model.Earlier > target; round++)
        {
            var earlier = model.Earlier;
            scroller.ChangeView(null, 0, null, disableAnimation: true);
            await Until("earlier messages while going far back", () => model.Earlier < earlier);
            await Drawn(list, model);
        }

        Expect(model.Earlier <= target, $"Going back stopped with {model.Earlier} messages still before the page.");
        Expect(list.Count <= MostOnPage, $"{list.Count} messages were on the page after going far back.");
        Expect(model.Later > 0, "The page still ended with the newest message after going far back.");
        Expect(Named(list, "MessageListNewest") is { Visibility: Visibility.Visible }, "Nothing led back to the newest messages.");
    }

    // A link to a message far from the page lands on it, in view, without everything between being made.
    private static async Task LandOnAMoment(MessageList list, MessageListViewModel model, ScrollViewer scroller)
    {
        Volatile.Write(ref stage, "landing on a moment");
        var wanted = model.Messages[model.Messages.Count / 2];
        model.Moment = Guid.Parse(wanted.Key);
        model.Update();
        ChatEntry? landed = null;
        await Until("the moment's message in view", () =>
        {
            landed = Entry(list, wanted.Key);
            return landed is { ActualHeight: > 0 } && ViewTop(landed, scroller) >= 0 && ViewTop(landed, scroller) < scroller.ViewportHeight;
        });
        Expect(list.Count <= MostOnPage, $"{list.Count} messages were made to land on one.");
        model.Moment = null;
        model.Update();
    }

    // Sending shows the newest message wherever the reader was, and what follows the newest is back.
    private static async Task ComeBackToNewest(MessageList list, MessageListViewModel model, ScrollViewer scroller)
    {
        Volatile.Write(ref stage, "coming back to the newest");
        model.ShowNewest();
        await Until("the newest message after sending", () =>
            model.Later == 0 && AtFoot(scroller) && Entry(list, model.Messages[^1].Key) is { ActualHeight: > 0 });
        Expect(list.Count <= MostOnPage, $"{list.Count} messages were on the page after coming back to the newest.");
        Expect(Named(list, "MessageListNewest") is { Visibility: Visibility.Collapsed }, "The way to the newest stayed at the newest.");
    }

    // Without scrolling: the action above the page shows what came before, by its name, from the keyboard.
    private static async Task StepBackByKeyboard(MessageList list, MessageListViewModel model)
    {
        Volatile.Write(ref stage, "stepping back by keyboard");

        // A conversation short enough to be shown whole has nothing before its page, and no action for it.
        if (model.Messages.Count <= FirstPage)
        {
            return;
        }

        if (Named(list, "MessageListEarlier") is not Button { Visibility: Visibility.Visible } earlier)
        {
            Fail("Nothing above the page showed earlier messages.");
            return;
        }

        Expect((string)earlier.Content == MessageListViewModel.EarlierLabel, "The action above the page has another name.");
        Expect(earlier.IsTabStop, "The action above the page cannot be reached by keyboard.");
        var before = model.Earlier;

        if (new ButtonAutomationPeer(earlier) is not Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider press)
        {
            Fail("The action above the page cannot be pressed by assistive technology.");
            return;
        }

        press.Invoke();
        await Until("earlier messages from the action", () => model.Earlier < before && list.Count > 0);
    }

    // The list draws a moment after its model changes. This waits until the page the model names is the one drawn
    // and laid out.
    private static async Task Drawn(MessageList list, MessageListViewModel model)
    {
        await Until("the page to be drawn", () =>
            model.Shown.Count == list.Count && (list.Count == 0 || Entry(list, model.Shown[0].Key) is { ActualHeight: > 0 }));
        await Settle(list);
        await Settle(list);
    }

    private static List<ChatMessage> Sample(int count)
    {
        List<ChatMessage> messages = [];
        for (var index = 0; index < count / 2; index++)
        {
            messages.Add(new(Guid.CreateVersion7().ToString(), true, $"Sample question number {index + 1} about the yard."));
            messages.Add(new(Guid.CreateVersion7().ToString(), false, SampleAnswer, Streaming: index == (count / 2) - 1));
        }

        return messages;
    }

    private static string SampleAnswer => """
        A sample answer with **bold words**, `inline code`, and a list:

        - a first sample point
        - a second sample point

        A closing sample sentence.
        """;

    private static bool AtFoot(ScrollViewer scroller) =>
        scroller.ScrollableHeight > 0 && scroller.VerticalOffset >= scroller.ScrollableHeight - 1;

    private static ChatEntry? Entry(MessageList list, string key) =>
        All<ChatEntry>(list).FirstOrDefault(entry => entry.Tag as string == key);

    private static ChatEntry? FirstInView(MessageList list, ScrollViewer scroller) =>
        All<ChatEntry>(list).FirstOrDefault(entry => entry.Parent is StackPanel && ViewTop(entry, scroller) >= 0);

    private static double ViewTop(ChatEntry entry, ScrollViewer scroller) =>
        entry.TransformToVisual(scroller).TransformPoint(new(0, 0)).Y;

    private static FrameworkElement? Named(MessageList list, string id) =>
        All<FrameworkElement>(list).FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id);

    private static void Expect(bool holds, string otherwise)
    {
        if (!holds)
        {
            Fail(otherwise);
        }
    }

    private static async Task Until(string what, Func<bool> holds)
    {
        for (var attempt = 0; attempt < 800; attempt++)
        {
            if (holds())
            {
                return;
            }

            await Task.Delay(25);
        }

        Fail($"Timed out waiting for {what}.");
    }

    private static Task Settle(UIElement root)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        root.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => done.TrySetResult());
        return done.Task;
    }

    private static List<T> All<T>(DependencyObject root)
        where T : DependencyObject
    {
        List<T> found = [];
        Collect(root, found);
        return found;
    }

    private static void Collect<T>(DependencyObject root, List<T> found)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                found.Add(match);
            }

            Collect(child, found);
        }
    }

    private static void Fail(string reason)
    {
        logger.LogError("Long conversation check failed: {Reason} (stage: {Stage}; {Standing})", reason, Volatile.Read(ref stage), standing());
        Exit(1);
    }

    private static void Exit(int code)
    {
        // What was said is written by another thread: it is let finish, so the result is in the log.
        Log.Factory.Dispose();

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsAndroid())
        {
            ExitImmediately(code);
        }

        Environment.Exit(code);
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "_exit")]
    private static extern void ExitImmediately(int code);

    #endregion
}
