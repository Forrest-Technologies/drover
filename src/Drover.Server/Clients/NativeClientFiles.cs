namespace Drover.Server.Clients;

/// <summary>
/// The native client's browser build, served beside the web client under <c>/native/</c> while screens move to it
/// (docs/adr/0015-one-native-client.md). Its files come from a folder the deployment fills with the published
/// <c>wwwroot</c>: the one <c>NativeClient:Path</c> names, or <c>native</c> in the server's web root. A server without
/// that folder answers nothing under <c>/native/</c>. The files are served under the same security headers as every
/// other response: nothing here widens the content security policy.
/// </summary>
public static class NativeClientFiles
{
    #region Private Fields

    public const string BasePath = "/native";

    private const string PathSetting = "NativeClient:Path";

    private const string DefaultFolder = "native";

    private const string Page = "index.html";

    private const string Binary = "application/octet-stream";

    #endregion

    #region Public Methods

    /// <summary>Serves the native client's files. Call it where the web client's files are served.</summary>
    public static StaticFileOptions UseNativeClientFiles(this WebApplication app)
    {
        var folder = app.Configuration[PathSetting] ?? Path.Combine(app.Environment.WebRootPath ?? app.Environment.ContentRootPath, DefaultFolder);
        var options = new StaticFileOptions
        {
            RequestPath = BasePath,
            FileProvider = Directory.Exists(folder) ? new PhysicalFileProvider(Path.GetFullPath(folder)) : new NullFileProvider(),
            ContentTypeProvider = ContentTypes(),
            OnPrepareResponse = PrecompressedFiles.Describe,
        };

        // The page names its files relative to the folder, so the folder is always asked for with its slash.
        app.Use((context, next) =>
        {
            if (context.Request.Path.Value == BasePath)
            {
                context.Response.Redirect($"{BasePath}/{context.Request.QueryString}");
                return Task.CompletedTask;
            }

            return next(context);
        });
        app.UseMiddleware<PrecompressedFiles>(options);
        app.UseStaticFiles(options);
        return options;
    }

    /// <summary>
    /// Every address of the native client that is not a file is its page, so an address can be opened, reloaded,
    /// and shared. A missing file stays missing: a script or a framework file is never answered with the page.
    /// </summary>
    public static void MapNativeClientPages(this WebApplication app, StaticFileOptions files)
    {
        app.MapFallbackToFile($"{BasePath}/{{*path:nonfile}}", $"{BasePath.TrimStart('/')}/{Page}", files);
    }

    #endregion

    #region Private Methods

    // The platform's runtime asks for files the web host does not know by default. A font's manifest is JSON.
    private static FileExtensionContentTypeProvider ContentTypes()
    {
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".manifest"] = "application/json";
        types.Mappings[".webmanifest"] = "application/manifest+json";
        types.Mappings[".wasm"] = "application/wasm";
        types.Mappings[".dll"] = Binary;
        types.Mappings[".pdb"] = Binary;
        types.Mappings[".webcil"] = Binary;
        types.Mappings[".dat"] = Binary;
        types.Mappings[".blat"] = Binary;
        types.Mappings[".clr"] = Binary;

        // A copy the build compressed. It is answered with the type of the file it stands for (PrecompressedFiles).
        types.Mappings[".br"] = Binary;
        types.Mappings[".gz"] = Binary;
        return types;
    }

    #endregion
}
