namespace Drover.Ui.Chat;

/// <summary>The native shell's actions for exporting a conversation file.</summary>
public interface IFileDelivery
{
    Task<bool> Save(SharedFile file, CancellationToken cancellationToken = default);

    Task Share(SharedFile file, CancellationToken cancellationToken = default);
}
