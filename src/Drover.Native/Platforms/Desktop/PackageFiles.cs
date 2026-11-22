namespace Drover.Native.Platform;

/// <summary>
/// Opens downloaded update packages so that a link or reparse point is refused on the opened handle itself, not by a
/// check of the name made before the open. A name changed between a check and an open can therefore never send a read
/// or a write to another file.
/// </summary>
internal static class PackageFiles
{
    #region Private Fields

    private const int AtCurrentDirectory = -100;

    private const int AtEmptyPath = 0x1000;

    private const int DoNotFollowLinks = 0x100;

    private const uint TypeAndInodeMask = 0x1 | 0x100;

    private const int TypeMask = 0xF000;

    private const int RegularFile = 0x8000;

    private const uint GenericRead = 0x80000000;

    private const uint ShareRead = 0x1;

    private const uint OpenExisting = 3;

    private const uint OpenReparsePoint = 0x00200000;

    private const uint Overlapped = 0x40000000;

    #endregion

    #region Public Methods

    /// <summary>
    /// Opens the package at <paramref name="path"/> for reading, or returns null when it is a link, a reparse point, or
    /// not a regular file. On Windows the open does not follow a reparse point, so the handle is checked; elsewhere the
    /// handle's device and inode must match the name's own entry, which is read without following a link.
    /// </summary>
    public static Microsoft.Win32.SafeHandles.SafeFileHandle? OpenForRead(string path, FileOptions options)
    {
        var handle = OperatingSystem.IsWindows()
            ? OpenWithoutFollowing(path, options)
            : File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, options);

        if (NamesOpenedFile(handle, path))
        {
            return handle;
        }

        handle.Dispose();
        return null;
    }

    /// <summary>
    /// Creates a new package file at <paramref name="path"/> for writing, shared with no one. The create fails if
    /// anything, a link included, is already there, and the opened file is checked as <see cref="OpenForRead"/> checks.
    /// </summary>
    public static Microsoft.Win32.SafeHandles.SafeFileHandle CreateForWrite(string path)
    {
        var handle = File.OpenHandle(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            FileOptions.Asynchronous);

        if (NamesOpenedFile(handle, path))
        {
            return handle;
        }

        handle.Dispose();
        throw new InvalidDataException(UpdatePackageTrust.LinkProblem);
    }

    /// <summary>
    /// True when <paramref name="handle"/> is a regular file that <paramref name="path"/> still names directly. A
    /// handle opened through a link, or a name replaced since the open, is refused.
    /// </summary>
    public static bool NamesOpenedFile(Microsoft.Win32.SafeHandles.SafeFileHandle handle, string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var attributes = File.GetAttributes(handle);
            return (attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0;
        }

        var opened = OpenedIdentity(handle);
        var named = NamedIdentity(path);
        return opened is not null && named is not null && IsSameRegularFile(opened.Value, named.Value);
    }

    /// <summary>
    /// True when the entry a name points at, read without following a link, is a regular file and is the file that
    /// was opened.
    /// </summary>
    public static bool IsSameRegularFile(FileIdentity opened, FileIdentity named) =>
        (named.Mode & TypeMask) == RegularFile
        && (opened.Mode & TypeMask) == RegularFile
        && opened.Device == named.Device
        && opened.Inode == named.Inode;

    #endregion

    #region Private Methods

    // A string passed to these functions is marshalled as UTF-8, the default on the systems that call them.

    private static Microsoft.Win32.SafeHandles.SafeFileHandle OpenWithoutFollowing(string path, FileOptions options)
    {
        var flags = OpenReparsePoint | (options.HasFlag(FileOptions.Asynchronous) ? Overlapped : 0);
        var handle = CreateFile(path, GenericRead, ShareRead, 0, OpenExisting, flags, 0);

        if (handle.IsInvalid)
        {
            var error = global::System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException($"The update package could not be opened (error {error}).");
        }

        return handle;
    }

    private static FileIdentity? OpenedIdentity(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        var added = false;

        try
        {
            handle.DangerousAddRef(ref added);
            var descriptor = (int)handle.DangerousGetHandle();
            return Identity(buffer => OperatingSystem.IsLinux()
                ? LinuxStatX(descriptor, string.Empty, AtEmptyPath, TypeAndInodeMask, buffer)
                : DarwinOpenedStat(descriptor, buffer));
        }
        finally
        {
            if (added)
            {
                handle.DangerousRelease();
            }
        }
    }

    private static FileIdentity? NamedIdentity(string path) => Identity(buffer => OperatingSystem.IsLinux()
        ? LinuxStatX(AtCurrentDirectory, path, DoNotFollowLinks, TypeAndInodeMask, buffer)
        : DarwinNamedStat(path, buffer));

    /// <summary>
    /// Reads one status record. Linux statx has one layout on every architecture: mode at 28, inode at 32, device
    /// major and minor at 136 and 140. The macOS record with 64-bit inodes has device at 0, mode at 4, inode at 8.
    /// </summary>
    private static FileIdentity? Identity(Func<nint, int> read)
    {
        var buffer = global::System.Runtime.InteropServices.Marshal.AllocHGlobal(256);

        try
        {
            if (read(buffer) != 0)
            {
                return null;
            }

            return OperatingSystem.IsLinux()
                ? new(
                    ((ulong)ReadUInt32(buffer, 136) << 32) | ReadUInt32(buffer, 140),
                    (ulong)global::System.Runtime.InteropServices.Marshal.ReadInt64(buffer, 32),
                    (ushort)global::System.Runtime.InteropServices.Marshal.ReadInt16(buffer, 28))
                : new(
                    ReadUInt32(buffer, 0),
                    (ulong)global::System.Runtime.InteropServices.Marshal.ReadInt64(buffer, 8),
                    (ushort)global::System.Runtime.InteropServices.Marshal.ReadInt16(buffer, 4));
        }
        finally
        {
            global::System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
        }
    }

    private static uint ReadUInt32(nint buffer, int offset) =>
        (uint)global::System.Runtime.InteropServices.Marshal.ReadInt32(buffer, offset);

    private static int DarwinOpenedStat(int descriptor, nint buffer) =>
        global::System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            is global::System.Runtime.InteropServices.Architecture.X64
            ? DarwinOpenedStatX64(descriptor, buffer)
            : DarwinOpenedStatArm64(descriptor, buffer);

    private static int DarwinNamedStat(string path, nint buffer) =>
        global::System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            is global::System.Runtime.InteropServices.Architecture.X64
            ? DarwinNamedStatX64(path, buffer)
            : DarwinNamedStatArm64(path, buffer);

    [global::System.Runtime.InteropServices.DllImport("libc", EntryPoint = "fstat")]
    private static extern int DarwinOpenedStatArm64(int descriptor, nint buffer);

    [global::System.Runtime.InteropServices.DllImport("libc", EntryPoint = "fstat$INODE64")]
    private static extern int DarwinOpenedStatX64(int descriptor, nint buffer);

    [global::System.Runtime.InteropServices.DllImport("libc", EntryPoint = "lstat")]
    private static extern int DarwinNamedStatArm64(
        string path,
        nint buffer);

    [global::System.Runtime.InteropServices.DllImport("libc", EntryPoint = "lstat$INODE64")]
    private static extern int DarwinNamedStatX64(
        string path,
        nint buffer);

    [global::System.Runtime.InteropServices.DllImport("libc", EntryPoint = "statx")]
    private static extern int LinuxStatX(
        int directory,
        string path,
        int flags,
        uint mask,
        nint buffer);

    [global::System.Runtime.InteropServices.DllImport(
        "kernel32",
        EntryPoint = "CreateFileW",
        CharSet = global::System.Runtime.InteropServices.CharSet.Unicode,
        SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
        string path,
        uint access,
        uint share,
        nint security,
        uint disposition,
        uint flags,
        nint template);

    #endregion
}


/// <summary>The device, inode, and type and mode bits of one file system entry.</summary>
internal readonly record struct FileIdentity(ulong Device, ulong Inode, ushort Mode);
