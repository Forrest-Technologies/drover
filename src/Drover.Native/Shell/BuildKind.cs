namespace Drover.Native.Shell;

/// <summary>Whether this is a development build. The component gallery is shown only in one.</summary>
public static class BuildKind
{
#if DEBUG || DROVER_GALLERY
    public static bool IsDevelopment => true;
#else
    public static bool IsDevelopment => false;
#endif
}
