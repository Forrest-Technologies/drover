namespace Drover.Presentation.Diagnostics;

/// <summary>The diagnostic log as text files in a folder of the app's own, made when first written to.</summary>
public sealed class DiagnosticFolder(string folder) : IDiagnosticStore
{
    #region Private Fields

    private static readonly ConcurrentDictionary<string, Lock> folderLocks = new(StringComparer.Ordinal);

    private readonly Lock gate = folderLocks.GetOrAdd(Path.GetFullPath(folder), static _ => new Lock());

    #endregion

    #region Interface Implementations

    public string? Read(string name)
    {
        lock (gate)
        {
            if (!Directory.Exists(folder))
            {
                return null;
            }

            using var processLock = AcquireProcessLock();
            var target = PathOf(name);
            return File.Exists(target) ? File.ReadAllText(target) : null;
        }
    }

    public void Write(string name, string text)
    {
        lock (gate)
        {
            Directory.CreateDirectory(folder);
            using var processLock = AcquireProcessLock();
            WriteCore(name, text);
        }
    }

    public void Append(string name, string text)
    {
        lock (gate)
        {
            Directory.CreateDirectory(folder);
            using var processLock = AcquireProcessLock();
            AppendCapped(name, text, DiagnosticLog.PartLimit);
        }
    }

    public void Append(string name, string olderName, string text, int limit)
    {
        lock (gate)
        {
            Directory.CreateDirectory(folder);
            using var processLock = AcquireProcessLock();
            var target = PathOf(name);
            var current = File.Exists(target) ? File.ReadAllText(target) : string.Empty;
            if (current.Length + text.Length > limit)
            {
                WriteCore(olderName, TailOf(current, limit));
                WriteCore(name, TailOf(text, limit));
                return;
            }

            File.AppendAllText(target, text);
        }
    }

    public void Remove(string name)
    {
        lock (gate)
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            using var processLock = AcquireProcessLock();
            File.Delete(PathOf(name));
        }
    }

    #endregion

    #region Private Methods

    private void AppendCapped(string name, string text, int limit)
    {
        var target = PathOf(name);
        var current = File.Exists(target) ? File.ReadAllText(target) : string.Empty;
        if (current.Length + text.Length > limit)
        {
            WriteCore(name, TailOf($"{current}{text}", limit));
            return;
        }

        File.AppendAllText(target, text);
    }

    private void WriteCore(string name, string text)
    {
        var target = PathOf(name);
        var temp = Path.Combine(folder, $"{name}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temp, text);
        File.Move(temp, target, overwrite: true);
    }

    private FileStream AcquireProcessLock()
    {
        var lockPath = Path.Combine(folder, ".lock");
        var deadline = Environment.TickCount64 + 5000;
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                if (!File.Exists(lockPath) || Environment.TickCount64 >= deadline)
                {
                    throw;
                }

                Thread.Sleep(5);
            }
        }
    }

    private static string TailOf(string text, int limit) =>
        text.Length <= limit ? text : text[^limit..];

    private string PathOf(string name) => Path.Combine(folder, $"{name}.txt");

    #endregion
}
