namespace Drover.Presentation.Tests.Theme;

/// <summary>The bundled color emoji font and its license ship together, with the exact bytes recorded in the
/// dependency list and no unused OpenType SVG table.</summary>
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

        var data = File.ReadAllBytes(font);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();
        var deps = File.ReadAllText(Path.Combine(Repository.Root, "docs", "architecture", "05-dependencies.md"));

        data.Length.Should().Be(5_044_436);
        hash.Should().Be("cfb9eec2d2feef7983f6caf293b7cdd4a7030b5404110da54a7b40357a0f95a6");
        deps.Should().Contain("5,044,436").And.Contain(hash);

        var count = (data[4] << 8) | data[5];
        var tables = Enumerable.Range(0, count).Select(index => Encoding.ASCII.GetString(data.AsSpan(12 + (16 * index), 4))).ToList();

        tables.Should().Contain("COLR").And.NotContain("SVG ", "the unused colour table is not bundled");
    }

    #endregion
}
