namespace Drover.Presentation.Tests.Support;

public sealed class UiLoopTests
{
    #region Public Methods

    [Fact]
    public void A_callback_after_disposal_is_ignored()
    {
        var loop = new UiLoop();
        loop.Dispose();

        var post = () => loop.Post(static _ => { }, null);

        post.Should().NotThrow();
    }

    #endregion
}
