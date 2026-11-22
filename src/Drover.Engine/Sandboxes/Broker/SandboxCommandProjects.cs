namespace Drover.Engine.Sandboxes.Broker;

/// <summary>
/// The project whose work is running a command in each sandbox right now, for the credential broker to decide whether
/// a project's secret applies. Held in this server's memory only: on a server with several processes, a request that
/// reaches a broker other than the one whose process runs the command finds no project and gets no project secret,
/// which fails closed.
/// </summary>
public sealed class SandboxCommandProjects
{
    #region Private Fields

    private readonly ConcurrentDictionary<Guid, ProjectHold> held = new();

    #endregion

    #region Public Methods

    /// <summary>Marks the sandbox as working for the project until the returned value is disposed.</summary>
    public IDisposable Hold(Guid sandboxId, Guid projectId)
    {
        held.AddOrUpdate(sandboxId, _ => new(projectId, 1), (_, current) => current with { Count = current.Count + 1, ProjectId = projectId });
        return new Release(this, sandboxId);
    }

    /// <summary>The project a command in the sandbox is running for now, or null.</summary>
    public Guid? ProjectOf(Guid sandboxId) => held.TryGetValue(sandboxId, out var hold) ? hold.ProjectId : null;

    #endregion

    #region Private Methods

    private void Drop(Guid sandboxId)
    {
        while (held.TryGetValue(sandboxId, out var current))
        {
            if (current.Count <= 1 ? held.TryRemove(new(sandboxId, current)) : held.TryUpdate(sandboxId, current with { Count = current.Count - 1 }, current))
            {
                return;
            }
        }
    }

    #endregion

    #region Nested Types

    private sealed record ProjectHold(Guid ProjectId, int Count);

    private sealed class Release(SandboxCommandProjects owner, Guid sandboxId) : IDisposable
    {
        #region Private Fields

        private int released;

        #endregion

        #region Interface Implementations

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                owner.Drop(sandboxId);
            }
        }

        #endregion
    }

    #endregion
}
