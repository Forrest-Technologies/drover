namespace Drover.Ui.Tests;

public sealed class ToastTests : BunitContext
{
    #region Private Fields

    private readonly ToastService toasts;

    #endregion

    #region Constructors

    public ToastTests()
    {
        Services.AddDroverUi();
        toasts = Services.GetRequiredService<ToastService>();
    }

    #endregion

    #region Public Methods

    [Fact]
    public void The_toast_service_is_one_per_scope()
    {
        using var scope = Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<ToastService>()
            .Should().BeSameAs(scope.ServiceProvider.GetRequiredService<ToastService>())
            .And.NotBeSameAs(toasts);
    }

    [Fact]
    public void Showing_a_toast_adds_it_as_info_by_default_and_says_so()
    {
        var changes = 0;
        toasts.Changed += () => changes++;

        toasts.Show("Settings saved");

        toasts.Toasts.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Message = "Settings saved", Kind = ToastKind.Info });
        changes.Should().Be(1);
    }

    [Fact]
    public void Dismissing_a_toast_removes_only_that_one()
    {
        toasts.Show("First");
        toasts.Show("Second");
        var changes = 0;
        toasts.Changed += () => changes++;

        toasts.Dismiss(toasts.Toasts[0].Id);
        toasts.Dismiss(toasts.Toasts[0].Id + 100);

        toasts.Toasts.Select(toast => toast.Message).Should().Equal("Second");
        changes.Should().Be(1);
    }

    [Fact]
    public void Both_live_regions_exist_before_any_toast_so_the_first_one_is_announced()
    {
        var host = Render<ToastHost>();

        host.Find("[role=status]").GetAttribute("aria-live").Should().Be("polite");
        host.FindAll("[role=alert]").Should().ContainSingle();
        host.FindAll(".dv-toast").Should().BeEmpty();
        host.Find("section").GetAttribute("aria-label").Should().Be("Notifications");
    }

    [Theory]
    [InlineData(ToastKind.Info, "Note")]
    [InlineData(ToastKind.Success, "Done")]
    [InlineData(ToastKind.Warning, "Warning")]
    public async Task A_toast_that_is_not_an_error_is_announced_politely_with_its_kind_in_words(ToastKind kind, string word)
    {
        var host = Render<ToastHost>();

        await host.InvokeAsync(() => toasts.Show("Sample message", kind));

        var toast = host.Find("[role=status] .dv-toast");
        toast.QuerySelector(".dv-toast-kind")?.TextContent.Should().Be(word);
        toast.QuerySelector("p")?.TextContent.Should().Contain("Sample message");
        host.FindAll("[role=alert] .dv-toast").Should().BeEmpty();
    }

    [Fact]
    public async Task An_error_toast_is_announced_as_an_alert_with_the_word_error()
    {
        var host = Render<ToastHost>();

        await host.InvokeAsync(() => toasts.Show("The model connection refused the key", ToastKind.Error));

        var toast = host.Find("[role=alert] .dv-toast");
        toast.QuerySelector(".dv-toast-kind")?.TextContent.Should().Be("Error");
        host.FindAll("[role=status] .dv-toast").Should().BeEmpty();
    }

    [Fact]
    public async Task Each_toast_has_a_real_dismiss_button_that_removes_it()
    {
        var host = Render<ToastHost>();
        await host.InvokeAsync(() => toasts.Show("First"));
        await host.InvokeAsync(() => toasts.Show("Second"));

        var dismiss = host.FindAll(".dv-toast")[0].QuerySelector("button");
        dismiss.Should().NotBeNull();
        dismiss.TextContent.Trim().Should().Be("Dismiss");
        dismiss.GetAttribute("type").Should().Be("button");
        await dismiss.ClickAsync(new MouseEventArgs());

        host.FindAll(".dv-toast p").Select(message => message.TextContent).Should().ContainSingle()
            .Which.Should().Contain("Second");
    }

    [Fact]
    public void A_host_that_is_gone_stops_listening()
    {
        var host = Render<ToastHost>();

        host.Instance.Dispose();
        var show = () => toasts.Show("Nobody is listening");

        show.Should().NotThrow();
        host.FindAll(".dv-toast").Should().BeEmpty();
    }

    #endregion
}
