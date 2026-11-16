namespace Drover.App;

public sealed class PreviewPushRegistration : IPushRegistration
{
    #region Interface Implementations

    public Task<DevicePushCheck> Check(CancellationToken cancellationToken = default) =>
        Task.FromResult(new DevicePushCheck(DevicePushSupport.NotSupported, null));

    public Task<PushDeviceRequest?> Subscribe(string publicKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<PushDeviceRequest?>(null);

    public Task<string?> Unsubscribe(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    #endregion
}
