namespace Drover.Native.UITests;

/// <summary>A build of the app made for the screen checks, on the target it runs on.</summary>
internal interface IAppUnderCheck
{
    /// <summary>Starts the app with the settings and returns the lines it reported, once it reported its result.</summary>
    Task<IReadOnlyList<string>> Run(IReadOnlyDictionary<string, string> settings, ILogger logger);
}
