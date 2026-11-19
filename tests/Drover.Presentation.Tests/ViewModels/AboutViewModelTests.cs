namespace Drover.Presentation.Tests.ViewModels;

/// <summary>Saving the diagnostic log from About, and the one offer after a failure that ended the app.</summary>
public sealed class AboutViewModelTests
{
    #region Private Fields

    private static readonly AppDescription app = new("1.2.3", "Test platform");

    private readonly MemoryDiagnosticStore store = new();

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 19, 9, 30, 0, TimeSpan.Zero));

    private readonly ToastService toasts = new(TimeProvider.System);

    private readonly List<(string Name, string Text)> saved = [];

    #endregion

    #region Public Methods

    [Fact]
    public async Task Saving_hands_the_device_the_composed_log_under_a_dated_name()
    {
        var model = Model();

        await model.SaveDiagnostics(Save);

        saved.Should().ContainSingle().Which.Name.Should().Be("drover-diagnostics-20261119-093000.txt");
        saved[0].Text.Should().Contain("App version: 1.2.3").And.Contain("Platform: Test platform");
        model.Notice.Should().Be("The diagnostic log was handed to this device to save.");
        model.NoticeIsProblem.Should().BeFalse();
    }

    [Fact]
    public async Task Choosing_nowhere_saves_nothing_and_says_nothing()
    {
        var model = Model();

        await model.SaveDiagnostics((_, _) => Task.FromResult(false));

        model.Notice.Should().BeNull();
    }

    [Fact]
    public async Task A_device_that_refuses_is_told_in_a_sentence_with_the_next_step()
    {
        var model = Model();

        await model.SaveDiagnostics((_, _) => throw new DeviceCallFailedException("Sample: refused."));

        model.Notice.Should().Be("The diagnostic log could not be saved. Try again and choose another place.");
        model.NoticeIsProblem.Should().BeTrue();
    }

    [Fact]
    public async Task After_a_failure_the_next_start_offers_the_log_once()
    {
        new DiagnosticLog(store, time).RecordFailure(new InvalidOperationException("sample"));

        Model().OfferAfterFailure(Save);
        Model().OfferAfterFailure(Save);

        var offer = toasts.Toasts.Should().ContainSingle().Subject;
        offer.Message.Should().Be(AboutViewModel.AfterFailure);
        offer.Kind.Should().Be(ToastKind.Warning);
        offer.Action!.Label.Should().Be("Save diagnostics");

        await offer.Action.Run();

        saved.Should().ContainSingle().Which.Text.Should().Contain(typeof(InvalidOperationException).FullName);
        toasts.Toasts.Should().Contain(toast => toast.Kind == ToastKind.Success && toast.Message == "The diagnostic log was handed to this device to save.");
    }

    [Fact]
    public void Without_a_failure_nothing_is_offered()
    {
        new DiagnosticLog(store, time).Write("warning only\n");

        Model().OfferAfterFailure(Save);

        toasts.Toasts.Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private AboutViewModel Model() => new(new(store, time), app, time, toasts, NullLogger<AboutViewModel>.Instance);

    private Task<bool> Save(string name, string text)
    {
        saved.Add((name, text));
        return Task.FromResult(true);
    }

    #endregion
}
