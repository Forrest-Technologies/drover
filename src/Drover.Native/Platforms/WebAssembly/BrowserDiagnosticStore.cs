namespace Drover.Native.Platform;

/// <summary>
/// The diagnostic log in the page's local storage, beside the theme choice. What is kept there is only what is safe
/// to save in a file (DiagnosticEntry), so it holds no token, cookie or message text. A browser can refuse storage;
/// the log then carries on without the entry.
/// </summary>
internal sealed class BrowserDiagnosticStore : IDiagnosticStore
{
    #region Interface Implementations

    public string? Read(string name) => Guarded(() => Browser.ReadStored(Key(name)));

    public void Write(string name, string text) => Guarded<object?>(() =>
    {
        Browser.Store(Key(name), text);
        return null;
    });

    public void Append(string name, string text)
    {
        var combined = $"{Read(name)}{text}";
        Write(name, combined.Length <= DiagnosticLog.PartLimit ? combined : combined[^DiagnosticLog.PartLimit..]);
    }

    public void Append(string name, string olderName, string text, int limit)
    {
        var current = Read(name) ?? string.Empty;
        if (current.Length + text.Length > limit)
        {
            Write(olderName, current.Length <= limit ? current : current[^limit..]);
            Write(name, text.Length <= limit ? text : text[^limit..]);
            return;
        }

        Append(name, text);
    }

    public void Remove(string name) => Guarded<object?>(() =>
    {
        Browser.Forget(Key(name));
        return null;
    });

    #endregion

    #region Private Methods

    private static string Key(string name) => $"drover.{name}";

    private static T Guarded<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (JSException exception)
        {
            throw new InvalidOperationException("The browser refused to use its local storage.", exception);
        }
    }

    #endregion
}
