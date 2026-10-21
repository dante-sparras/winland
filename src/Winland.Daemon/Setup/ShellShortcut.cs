using System.Runtime.InteropServices;
using System.Text;
using Winland.Install;

namespace Winland.Daemon;

/// <summary>Writes a Start menu shortcut that carries Winland's AppUserModelID.</summary>
sealed class ShellStartMenu : IStartMenu
{
    public void Place(Shortcut shortcut)
    {
        var directory = Path.GetDirectoryName(shortcut.LinkPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        ShellShortcut.Write(shortcut.LinkPath, shortcut.Target, shortcut.Arguments, shortcut.AppUserModelId);
    }

    public void Remove(string linkPath)
    {
        if (File.Exists(linkPath))
            File.Delete(linkPath);
    }
}

static class ShellShortcut
{
    private static readonly PropertyKey AppUserModelId = new()
    {
        FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        PropertyId = 5,
    };

    public static void Write(string linkPath, string target, string arguments, string appUserModelId)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            link.SetPath(target);
            link.SetArguments(arguments);
            link.SetWorkingDirectory(Path.GetDirectoryName(target) ?? "");
            link.SetDescription("Winland");
            link.SetIconLocation(target, 0);
            link.SetShowCmd(1);

            var store = (IPropertyStore)link;
            // VT_LPWSTR. PropVariantClear frees the CoTaskMem string.
            var variant = new PropVariant
            {
                VariantType = 31,
                Pointer = Marshal.StringToCoTaskMemUni(appUserModelId),
            };
            try
            {
                var key = AppUserModelId;
                store.SetValue(ref key, ref variant);
                store.Commit();
            }
            finally
            {
                _ = PropVariantClear(ref variant);
            }

            ((IPersistFile)link).Save(linkPath, true);
        }
        finally
        {
            _ = Marshal.ReleaseComObject(link);
        }
    }

    public static string ReadAppUserModelId(string linkPath)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            ((IPersistFile)link).Load(linkPath, 0);
            var store = (IPropertyStore)link;
            var key = AppUserModelId;
            store.GetValue(ref key, out var variant);
            try
            {
                if (variant.VariantType != 31 || variant.Pointer == IntPtr.Zero)
                    return "";

                return Marshal.PtrToStringUni(variant.Pointer) ?? "";
            }
            finally
            {
                _ = PropVariantClear(ref variant);
            }
        }
        finally
        {
            _ = Marshal.ReleaseComObject(link);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant variant);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    // vt at 0, the string pointer at 8. 24 bytes matches the 64-bit PROPVARIANT.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort VariantType;

        [FieldOffset(8)]
        public IntPtr Pointer;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int capacity, IntPtr findData, int flags);

        void GetIdList(out IntPtr list);

        void SetIdList(IntPtr list);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int capacity);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int capacity);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int capacity);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);

        void GetHotkey(out ushort hotkey);

        void SetHotkey(ushort hotkey);

        void GetShowCmd(out int show);

        void SetShowCmd(int show);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int capacity, out int icon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string file, int icon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);

        void Resolve(IntPtr window, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassId(out Guid classId);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);

        void Save([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.Bool)] bool remember);

        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);

        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string file);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint count);

        void GetAt(uint index, out PropertyKey key);

        void GetValue(ref PropertyKey key, out PropVariant value);

        void SetValue(ref PropertyKey key, ref PropVariant value);

        void Commit();
    }
}
