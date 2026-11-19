namespace Drover.Native.Platform;

/// <summary>
/// Where a browser keeps the diagnostic log: the page's local storage, beside the theme choice. What is kept there is
/// what is safe to save in a file (DiagnosticEntry), so it holds no token, cookie or message text.
/// </summary>
internal static class PlatformDiagnostics
{
    #region Properties

    public static string Kind => "Browser";

    #endregion

    #region Public Methods

    public static IDiagnosticStore Store() => new BrowserDiagnosticStore();

    #endregion

    #region Browser Storage

    private sealed class BrowserDiagnosticStore : IDiagnosticStore
    {
        public string? Read(string name) => Guarded(() => Browser.ReadStored(Key(name)));

        public void Write(string name, string text) => Guarded<object?>(() =>
        {
            Browser.Store(Key(name), text);
            return null;
        });

        public void Append(string name, string text) => Write(name, $"{Read(name)}{text}");

        public void Remove(string name) => Guarded<object?>(() =>
        {
            Browser.Forget(Key(name));
            return null;
        });

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
    }

    #endregion
}
