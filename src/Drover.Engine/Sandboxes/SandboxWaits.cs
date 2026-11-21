namespace Drover.Engine.Sandboxes;

/// <summary>The wait a provider asked for, in the words a person can act on.</summary>
public static class SandboxWaits
{
    #region Public Methods

    public static string Words(TimeSpan? wait)
    {
        if (wait is not { } duration || duration <= TimeSpan.Zero)
        {
            return "in a moment";
        }

        if (duration <= TimeSpan.FromSeconds(90))
        {
            var seconds = (int)Math.Ceiling(duration.TotalSeconds / 5) * 5;
            return $"in about {seconds} seconds";
        }

        if (duration <= TimeSpan.FromMinutes(60))
        {
            var minutes = (int)Math.Ceiling(duration.TotalMinutes);
            return $"in about {minutes} minutes";
        }

        return "later";
    }

    #endregion
}
