namespace Drover.Presentation.Diagnostics;

/// <summary>What a diagnostic log says about the app that wrote it: its version and the platform it runs on.</summary>
public sealed record AppDescription(string Version, string Platform);
