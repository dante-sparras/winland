using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Winland.Plugins;

/// <summary>
/// Opens files and folders by handle, optionally without following a reparse point, and reads or
/// writes what the handle refers to. An elevated program that checks a path and then uses it again
/// can be sent somewhere else in between by a junction or a rename. Working on one handle cannot.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsFileHandles
{
    public const uint ReadAttributes = 0x0080;

    public const uint ReadControl = 0x00020000;

    public const uint WriteDac = 0x00040000;

    public const uint WriteOwner = 0x00080000;

    public const uint Delete = 0x00010000;

    public const uint GenericRead = 0x80000000;

    public const uint GenericWrite = 0x40000000;

    private const uint OpenExisting = 3;
    private const uint CreateNew = 1;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint OwnerSecurity = 0x00000001;
    private const uint DaclSecurity = 0x00000004;
    private const uint ProtectedDaclSecurity = 0x80000000;
    private const int FileRenameInformation = 10;
    private const int FileDispositionInfo = 4;

    /// <summary>
    /// Opens an existing file or folder. With <paramref name="followReparse"/> false a junction or a
    /// symbolic link is opened itself, so <see cref="Attributes"/> shows it rather than its target.
    /// </summary>
    public static SafeFileHandle Open(string path, uint access, FileShare share, bool followReparse)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Create(path, access, share, OpenExisting, BackupSemantics | (followReparse ? 0 : OpenReparsePoint));
    }

    /// <summary>
    /// Creates a new file and fails when the name is taken, by a file or by a link. The handle may write.
    /// </summary>
    public static SafeFileHandle CreateNewFile(string path, FileShare share)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Create(path, GenericRead | GenericWrite | Delete, share, CreateNew, OpenReparsePoint);
    }

    public static FileAttributes Attributes(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!GetFileInformationByHandle(handle, out var information))
            throw Failure(Marshal.GetLastPInvokeError(), "the open file");

        return (FileAttributes)information.FileAttributes;
    }

    /// <summary>The path the handle refers to now, with junctions resolved, as <c>C:\...</c> or <c>\\?\UNC\...</c>.</summary>
    public static string FinalPath(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        var buffer = new char[512];
        while (true)
        {
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
            if (length == 0)
                throw Failure(Marshal.GetLastPInvokeError(), "the open file");

            if (length < buffer.Length)
            {
                var path = new string(buffer, 0, (int)length);
                return path.StartsWith(@"\\?\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)
                    ? path[4..]
                    : path;
            }

            buffer = new char[length + 1];
        }
    }

    /// <summary>Reads the owner and the access list of what the handle refers to, as a self-relative descriptor.</summary>
    public static byte[] ReadSecurity(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        const uint wanted = OwnerSecurity | DaclSecurity;
        _ = GetKernelObjectSecurity(handle, wanted, null, 0, out var needed);
        var error = Marshal.GetLastPInvokeError();
        if (needed == 0)
            throw Failure(error, "the open file");

        var descriptor = new byte[needed];
        if (!GetKernelObjectSecurity(handle, wanted, descriptor, needed, out _))
            throw Failure(Marshal.GetLastPInvokeError(), "the open file");

        return descriptor;
    }

    /// <summary>
    /// Writes the owner and a protected access list onto what the handle refers to. Children keep
    /// the entries they have: nothing walks into the folder by name.
    /// </summary>
    public static void WriteSecurity(SafeFileHandle handle, byte[] descriptor)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!SetKernelObjectSecurity(handle, OwnerSecurity | DaclSecurity | ProtectedDaclSecurity, descriptor))
            throw Failure(Marshal.GetLastPInvokeError(), "the open folder");
    }

    /// <summary>
    /// Gives the open file the name <paramref name="name"/> in the open folder <paramref name="directory"/>,
    /// replacing an entry of that name. The name is looked up in that folder only, so no junction
    /// on the way to it is followed.
    /// </summary>
    public static void Rename(SafeFileHandle file, SafeFileHandle directory, string name)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.AsSpan().ContainsAny(@"\/:"))
            throw new ArgumentException("The new name must be a single name.", nameof(name));

        // FILE_RENAME_INFORMATION: a BOOLEAN padded to the pointer, the root folder handle, the name
        // length in bytes, then the name. The kernel call takes a name relative to the root handle as
        // it is; the Win32 wrapper turns it into a path first.
        var nameOffset = (IntPtr.Size * 2) + sizeof(uint);
        var size = nameOffset + ((name.Length + 1) * sizeof(char));
        var buffer = Marshal.AllocHGlobal(size);
        var status = new IntPtr[2];
        var added = false;
        try
        {
            directory.DangerousAddRef(ref added);
            for (var i = 0; i < size; i++)
                Marshal.WriteByte(buffer, i, 0);

            Marshal.WriteByte(buffer, 0, 1);
            Marshal.WriteIntPtr(buffer, IntPtr.Size, directory.DangerousGetHandle());
            Marshal.WriteInt32(buffer, IntPtr.Size * 2, name.Length * sizeof(char));
            Marshal.Copy(name.ToCharArray(), 0, buffer + nameOffset, name.Length);
            var result = NtSetInformationFile(file, status, buffer, (uint)size, FileRenameInformation);
            if (result < 0)
                throw Failure((int)RtlNtStatusToDosError(result), name);
        }
        finally
        {
            if (added)
                directory.DangerousRelease();

            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Deletes what the handle refers to once every handle to it is closed. The handle needs <see cref="Delete"/>.</summary>
    public static void MarkForDeletion(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(buffer, 1);
            if (!SetFileInformationByHandle(handle, FileDispositionInfo, buffer, sizeof(int)))
                throw Failure(Marshal.GetLastPInvokeError(), "the open file");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static SafeFileHandle Create(string path, uint access, FileShare share, uint disposition, uint flags)
    {
        var handle = CreateFileW(path, access, (uint)share, IntPtr.Zero, disposition, flags, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw Failure(error, path);
        }

        return handle;
    }

    private static Exception Failure(int error, string what) => error switch
    {
        2 => new FileNotFoundException($"'{what}' was not found.", what),
        3 => new DirectoryNotFoundException($"A folder on the way to '{what}' was not found."),
        5 => new UnauthorizedAccessException($"Access to '{what}' was denied."),
        _ => new IOException($"'{what}': {Marshal.GetPInvokeErrorMessage(error)}", unchecked((int)0x80070000 | error)),
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string name,
        uint access,
        uint share,
        IntPtr security,
        uint disposition,
        uint flags,
        IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, [Out] char[] path, uint length, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKernelObjectSecurity(SafeFileHandle handle, uint requested, [Out] byte[]? descriptor, uint length, out uint needed);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKernelObjectSecurity(SafeFileHandle handle, uint information, byte[] descriptor);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, IntPtr information, uint size);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(SafeFileHandle handle, [Out] IntPtr[] status, IntPtr information, uint length, int informationClass);

    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);
}
