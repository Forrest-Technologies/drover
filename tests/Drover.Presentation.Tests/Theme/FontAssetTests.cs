namespace Drover.Presentation.Tests.Theme;

/// <summary>The bundled color emoji font and its license ship together, and the dependency list records the size.</summary>
public sealed class FontAssetTests
{
    #region Assets

    [Fact]
    public void The_color_emoji_font_ships_with_its_license_and_a_recorded_size()
    {
        var font = Path.Combine(Repository.NativeFolder, "Assets", "Fonts", "NotoColorEmoji-Regular.ttf");
        var license = Path.Combine(Repository.NativeFolder, "Assets", "Fonts", "NotoColorEmoji-LICENSE.txt");

        File.Exists(font).Should().BeTrue("the browser build needs the bundled font");
        File.Exists(license).Should().BeTrue("a bundled font ships with its license");
        File.ReadAllText(license).Should().Contain("SIL Open Font License, Version 1.1");

        var bytes = new FileInfo(font).Length;
        bytes.Should().BeGreaterThan(0).And.BeLessThan(30_000_000);
        File.ReadAllText(Path.Combine(Repository.Root, "docs", "architecture", "05-dependencies.md"))
            .Should().Contain(bytes.ToString("N0", CultureInfo.InvariantCulture), "the exact size is recorded in the dependency list");
    }

    #endregion
}
