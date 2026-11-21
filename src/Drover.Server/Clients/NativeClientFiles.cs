namespace Drover.Server.Clients;

/// <summary>
/// The browser client: the native client's browser build, served at the root. Its files come from a folder the
/// deployment fills with the published <c>wwwroot</c>: the one <c>NativeClient:Path</c> names, or the server's web
/// root. A server without that folder serves the API alone. The files are served under the same security headers as
/// every other response: nothing here widens the content security policy. The address the build was served under
/// before it took the root, <c>/native/</c>, is kept as a way in (<see cref="RetiredNativeAddress"/>).
/// </summary>
public static class NativeClientFiles
{
    #region Private Fields

    private const string PathSetting = "NativeClient:Path";

    private const string Page = "index.html";

    private const string Binary = "application/octet-stream";

    #endregion

    #region Public Methods

    /// <summary>Serves the client's files, and sends the retired address to the root.</summary>
    public static StaticFileOptions UseNativeClientFiles(this WebApplication app)
    {
        var folder = app.Configuration[PathSetting] ?? app.Environment.WebRootPath;
        var options = new StaticFileOptions
        {
            FileProvider = Directory.Exists(folder) ? new PhysicalFileProvider(Path.GetFullPath(folder)) : new NullFileProvider(),
            ContentTypeProvider = ContentTypes(),
            OnPrepareResponse = PrecompressedFiles.Describe,
        };

        app.UseMiddleware<RetiredNativeAddress>();
        app.UseMiddleware<PrecompressedFiles>(options);
        app.UseStaticFiles(options);
        return options;
    }

    /// <summary>
    /// Every address that is not a file is the client's page, so an address can be opened, reloaded, and shared. A
    /// missing file stays missing: a script or a framework file is never answered with the page.
    /// </summary>
    public static void MapNativeClientPages(this WebApplication app, StaticFileOptions files) =>
        app.MapFallbackToFile(Page, files);

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
