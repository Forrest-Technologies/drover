namespace Drover.Domain.Tests;

public sealed class FileStorageKeysTests
{
    #region Tests

    [Fact]
    public void A_key_is_the_space_and_the_file_and_belongs_to_that_space_only()
    {
        var space = Guid.CreateVersion7();
        var file = Guid.CreateVersion7();

        var key = FileStorageKeys.For(space, file);

        key.Should().Be($"{space:N}/{file:N}");
        FileStorageKeys.BelongsTo(space, key).Should().BeTrue();
        FileStorageKeys.Require(space, key).Should().Be(file);
        FileStorageKeys.BelongsTo(Guid.CreateVersion7(), key).Should().BeFalse();
        var other = () => FileStorageKeys.Require(Guid.CreateVersion7(), key);
        other.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../etc/passwd")]
    public void Anything_else_belongs_to_no_space(string? key) =>
        FileStorageKeys.BelongsTo(Guid.CreateVersion7(), key).Should().BeFalse();

    #endregion
}
