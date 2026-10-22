using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Winland.Infrastructure;

// A Store app's top-level window belongs to ApplicationFrameHost. The app itself
// is a CoreWindow child in another process, and that child is created after the
// frame. A frame that does not host an app yet is not a window to adopt.
internal static class PackagedIdentity
{
    private const string FrameClass = "ApplicationFrameWindow";
    internal const string FrameExe = "ApplicationFrameHost.exe";
    private const string CoreClass = "Windows.UI.Core.CoreWindow";

    private static readonly PROPERTYKEY AppUserModelId = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5,
    };

    private static readonly Lock Gate = new();
    private static readonly Dictionary<long, CacheEntry> Cache = [];

    // ProcessId is the hosted app's process for a frame that hosts one, and the window's own process otherwise.
    internal readonly record struct Resolved(string Class, string Exe, string Aumid, bool UnresolvedFrame, uint ProcessId);

    private readonly record struct CacheEntry(uint ProcessId, Resolved Identity);

    internal static Resolved Resolve(HWND hwnd, string className, string exe)
    {
        var processId = ProcessId(hwnd);
        var key = ((nint)hwnd).ToInt64();
        lock (Gate)
        {
            ForgetClosed();

            // A frame that hosted no app yet is looked at again every time. Its app's CoreWindow can be
            // attached after the frame was titled, and a cached answer would still say no app when it is shown.
            if (Cache.TryGetValue(key, out var cached)
                && cached.ProcessId == processId
                && !cached.Identity.UnresolvedFrame)
            {
                return cached.Identity;
            }
        }

        var identity = Read(hwnd, className, exe, processId);
        lock (Gate)
            Cache[key] = new CacheEntry(processId, identity);

        return identity;
    }

    private static Resolved Read(HWND hwnd, string className, string exe, uint processId)
    {
        if (!IsFrame(className, exe))
            return new Resolved(className, exe, Aumid(processId, hwnd), UnresolvedFrame: false, processId);

        if (!TryFindHostedChild(hwnd, processId, out var child, out var childProcess))
            return new Resolved(className, exe, "", UnresolvedFrame: true, processId);

        var childExe = ExeFile(childProcess);
        var aumid = Aumid(childProcess, child);
        if (aumid.Length == 0)
            aumid = AumidFromWindow(hwnd);

        return new Resolved(WindowAdapter.ClassName(child), childExe, aumid, UnresolvedFrame: false, childProcess);
    }

    /// <summary>
    /// The process that owns a window's content, read now: the hosted app for a Store frame, 0 for a
    /// frame that hosts none, and the window's own process for anything else.
    /// </summary>
    internal static uint OwnerProcess(HWND hwnd)
    {
        var processId = ProcessId(hwnd);
        if (processId == 0 || !IsFrame(WindowAdapter.ClassName(hwnd), ExeFile(processId)))
            return processId;

        return TryFindHostedChild(hwnd, processId, out _, out var childProcess) ? childProcess : 0;
    }

    private static bool IsFrame(string className, string exe) =>
        className == FrameClass || exe.Equals(FrameExe, StringComparison.OrdinalIgnoreCase);

    private static bool TryFindHostedChild(HWND parent, uint hostProcess, out HWND child, out uint childProcess)
    {
        HWND found = default;
        uint foundProcess = 0;
        _ = PInvoke.EnumChildWindows(parent, (hwnd, _) =>
        {
            if (WindowAdapter.ClassName(hwnd) != CoreClass)
                return true;

            var process = ProcessId(hwnd);
            if (process == 0 || process == hostProcess)
                return true;

            found = hwnd;
            foundProcess = process;
            return false;
        }, default);

        child = found;
        childProcess = foundProcess;
        return !found.IsNull;
    }

    private static string Aumid(uint processId, HWND hwnd)
    {
        var fromProcess = AumidFromProcess(processId);
        return fromProcess.Length > 0 ? fromProcess : AumidFromWindow(hwnd);
    }

    private static string AumidFromProcess(uint processId)
    {
        if (processId == 0)
            return "";

        using var process = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            processId);
        if (process is null || process.IsInvalid)
            return "";

        uint length = 0;
        var probe = PInvoke.GetApplicationUserModelId(process, ref length, []);
        if (probe != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER || length <= 1)
            return "";

        var buffer = length <= 512 ? stackalloc char[(int)length] : new char[length];
        if (PInvoke.GetApplicationUserModelId(process, ref length, buffer) != WIN32_ERROR.ERROR_SUCCESS)
            return "";

        var written = (int)length;
        if (written > buffer.Length)
            written = buffer.Length;

        while (written > 0 && buffer[written - 1] == '\0')
            written--;

        return written == 0 ? "" : new string(buffer[..written]);
    }

    private static string AumidFromWindow(HWND hwnd)
    {
        if (hwnd.IsNull || PInvoke.SHGetPropertyStoreForWindow(hwnd, out IPropertyStore? store).Failed || store is null)
            return "";

        try
        {
            var key = AppUserModelId;
            store.GetValue(in key, out var value);
            try
            {
                Span<char> buffer = stackalloc char[260];
                if (PInvoke.PropVariantToString(in value, buffer).Failed)
                    return "";

                var end = buffer.IndexOf('\0');
                if (end < 0)
                    end = buffer.Length;

                return end == 0 ? "" : new string(buffer[..end]);
            }
            finally
            {
                _ = PInvoke.PropVariantClear(ref value);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return "";
        }
        finally
        {
            _ = System.Runtime.InteropServices.Marshal.ReleaseComObject(store);
        }
    }

    internal static string ExeFile(uint processId)
    {
        var path = ImagePath(processId);
        return path.Length == 0 ? "" : Path.GetFileName(path);
    }

    /// <summary>The program's full path, or empty when it cannot be read. <see cref="ExeFile"/> is that path's file name.</summary>
    internal static string ImagePath(uint processId)
    {
        if (processId == 0)
            return "";

        using var process = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            processId);
        if (process is not null && !process.IsInvalid)
        {
            Span<char> buffer = stackalloc char[1024];
            var size = (uint)buffer.Length;
            if (PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref size)
                && size > 0
                && size <= buffer.Length)
            {
                return new string(buffer[..(int)size]);
            }
        }

        try
        {
            using var managed = Process.GetProcessById((int)processId);
            return managed.MainModule?.FileName ?? "";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return "";
        }
    }

    private static uint ProcessId(HWND hwnd)
    {
        if (PInvoke.GetWindowThreadProcessId(hwnd, out var processId) == 0)
            return 0;

        return processId;
    }

    private static void ForgetClosed()
    {
        if (Cache.Count == 0)
            return;

        List<long>? stale = null;
        foreach (var key in Cache.Keys)
        {
            if (!PInvoke.IsWindow(new HWND((nint)key)))
                (stale ??= []).Add(key);
        }

        if (stale is null)
            return;

        foreach (var key in stale)
            Cache.Remove(key);
    }
}
