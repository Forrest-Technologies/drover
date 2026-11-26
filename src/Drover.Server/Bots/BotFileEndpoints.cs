namespace Drover.Server.Bots;

/// <summary>
/// Bots as files, under the tenant scope filter of the bots group: an admin writes a bot down, has a file read, and
/// imports it. A file holds a bot's words and nothing it may do, and an imported bot starts paused.
/// </summary>
public static class BotFileEndpoints
{
    #region Private Fields

    // The file travels as a JSON string, which can double its size, with a little room for the rest of the request.
    private const int MaxRequestBytes = (BotFileReader.MaxFileBytes * 2) + 4096;

    private static readonly string tooLarge = $"This file is larger than a bot file can be ({BotFileReader.MaxFileBytes / 1024} KB).";

    #endregion

    #region Public Methods

    public static RouteGroupBuilder MapBotFileEndpoints(this RouteGroupBuilder bots)
    {
        bots.MapGet("/{botId:guid}/export", ExportBot).RequireSpaceRole(MembershipRole.Admin).WithName("ExportBot")
            .WithSummary("The bot as a file to share: its name, what it is for, its instructions, and its model as a suggestion. Nothing else of the bot.");
        bots.MapPost("/import/preview", PreviewBotImport).RequireSpaceRole(MembershipRole.Admin).WithName("PreviewBotImport")
            .WithBodyLimit(MaxRequestBytes, _ => tooLarge);
        bots.MapPost("/import", ImportBot).RequireSpaceRole(MembershipRole.Admin).WithName("ImportBot")
            .WithSummary("Adds the bot a file holds. It starts paused, with the tools every new bot starts with.")
            .WithBodyLimit(MaxRequestBytes, _ => tooLarge);
        return bots;
    }

    #endregion

    #region Handlers

    private static async Task<Results<Ok<BotFile>, NotFound>> ExportBot(Guid botId, HttpContext context, BotFileService files) =>
        await files.Export(botId, context.RequestAborted) is { } file
            ? TypedResults.Ok(file)
            : TypedResults.NotFound();

    private static async Task<Results<Ok<BotImportPreview>, ProblemHttpResult>> PreviewBotImport(BotFileText request, HttpContext context, BotFileService files)
    {
        var read = await files.Preview(request.File, context.RequestAborted);
        return read.Value is { } preview
            ? TypedResults.Ok(preview)
            : TypedResults.Problem(title: read.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Created<BotSummary>, ProblemHttpResult>> ImportBot(BotFileText request, HttpContext context, BotFileService files)
    {
        var access = context.GetSpaceAccess();
        var imported = await files.Import(request.File, access.UserId, context.RequestAborted);
        return imported.Value is { } bot
            ? TypedResults.Created($"/api/spaces/{access.SpaceId}/bots/{bot.Id}", bot.ToSummary())
            : TypedResults.Problem(title: imported.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    #endregion
}
