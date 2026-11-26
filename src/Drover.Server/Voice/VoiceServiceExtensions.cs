namespace Drover.Server.Voice;

public static class VoiceServiceExtensions
{
    #region Private Fields

    public const string SpeechAddressSetting = "Drover:Voice:Speech:BaseUrl";

    public const string SpeechModelSetting = "Drover:Voice:Speech:Model";

    public const string SpeechVoiceSetting = "Drover:Voice:Speech:Voice";

    public const string TranscriptionAddressSetting = "Drover:Voice:Transcription:BaseUrl";

    public const string TranscriptionModelSetting = "Drover:Voice:Transcription:Model";

    #endregion

    #region Public Methods

    /// <summary>
    /// Registers speech. Every space's own speech models go through the model provider adapters. The server's own
    /// speech service is optional: <c>Drover:Voice:Speech:BaseUrl</c> names a compatible speech endpoint that reads
    /// aloud (with <c>:Model</c>, default <c>kokoro</c>, and <c>:Voice</c>, default <c>af_heart</c>), and
    /// <c>Drover:Voice:Transcription:BaseUrl</c> one that writes down recordings (its <c>:Model</c> is required).
    /// Both are the operator's own configuration, so they may be private addresses where the deployment allows
    /// private addresses, and their client follows no redirect, so that permission never reaches another origin.
    /// A space with no speech model of its own falls back to them.
    /// </summary>
    public static IServiceCollection AddDroverVoice(this IServiceCollection services, IConfiguration configuration)
    {
        var server = ReadServerVoice(configuration);
        services.AddSingleton(server);
        services.AddSingleton<VoiceService>();

        if (server is { SpeechAddress: null, TranscriptionAddress: null })
        {
            return services;
        }

        services.TryAddTransient<OperatorAddressHandler>();
        services.AddOperatorSpeech()
            .ConfigurePrimaryHttpMessageHandler(provider =>
                OutboundHttp.CreateHandlerWithoutRedirects(provider.GetRequiredService<IOptions<DeploymentPolicyOptions>>().Value.Rules))
            .AddHttpMessageHandler<OperatorAddressHandler>();
        return services;
    }

    public static ServerVoiceOptions ReadServerVoice(IConfiguration configuration)
    {
        var transcription = ReadAddress(configuration, TranscriptionAddressSetting, "writes down recordings");
        var transcriptionModel = Text(configuration, TranscriptionModelSetting);

        if (transcription is not null && transcriptionModel is null)
        {
            throw new InvalidOperationException(
                $"{TranscriptionAddressSetting} is set, so {TranscriptionModelSetting} must name the model that writes down recordings. Set it, or remove the address.");
        }

        return new ServerVoiceOptions
        {
            SpeechAddress = ReadAddress(configuration, SpeechAddressSetting, "reads text aloud"),
            SpeechModel = Text(configuration, SpeechModelSetting) ?? ServerVoiceOptions.DefaultSpeechModel,
            SpeechVoice = Text(configuration, SpeechVoiceSetting) ?? ServerVoiceOptions.DefaultSpeechVoice,
            TranscriptionAddress = transcription,
            TranscriptionModel = transcriptionModel,
        };
    }

    #endregion

    #region Private Methods

    private static string? Text(IConfiguration configuration, string setting) =>
        configuration[setting]?.Trim() is { Length: > 0 } value ? value : null;

    private static Uri? ReadAddress(IConfiguration configuration, string setting, string does) =>
        configuration[setting] switch
        {
            null or "" => null,
            var text when Uri.TryCreate(text.Trim(), UriKind.Absolute, out var address) && address.Scheme is "http" or "https"
                && address.UserInfo.Length == 0 => address,
            _ => throw new InvalidOperationException(
                $"{setting} must be the http or https address of a speech service that {does}, without credentials in it. Correct it, or remove it to run without one."),
        };

    #endregion
}
