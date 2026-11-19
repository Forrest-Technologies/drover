namespace Drover.Server.Tests;

public sealed class PreviewConfigurationTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Tests

    [Fact]
    public async Task Missing_preview_configuration_starts_and_explains_that_download_still_works()
    {
        var previews = Services.GetRequiredService<Drover.Server.Files.PreviewFileService>();

        var response = await previews.Mint(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Token);

        response.Should().Be(new FilePreviewResponse(null,
            "HTML preview is unavailable. The server operator must configure a separate preview origin. Download is still available."));
    }

    #endregion
}
