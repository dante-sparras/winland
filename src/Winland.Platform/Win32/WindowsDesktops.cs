using System.Runtime.InteropServices;

namespace Winland.Platform.Win32;

/// <summary>
/// Windows' own virtual desktops, which are not Winoma's Workspaces (ADR 0003). Winoma never makes, removes, or switches one.
/// It asks one thing, through <c>IVirtualDesktopManager</c>, the interface Windows documents: whether a window is on the desktop in front.
/// A window Windows keeps on another desktop reads the same cloak as one Winoma hid, so the cloak alone does not say whose it is.
/// </summary>
internal static unsafe class WindowsDesktops
{
    // CLSID_VirtualDesktopManager and IID_IVirtualDesktopManager, from shobjidl_core.h.
    private static readonly Guid ManagerClass = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");
    private static readonly Guid ManagerInterface = new("A5CD92FF-29BE-454C-8D04-D82879FB3F1B");
    private const int ClsctxAll = 0x17;

    // IVirtualDesktopManager::IsWindowOnCurrentVirtualDesktop, the first method after the three of IUnknown.
    private const int IsOnCurrentSlot = 3;

    /// <summary>True when Windows keeps <paramref name="hwnd"/> on a virtual desktop that is not in front.</summary>
    /// <param name="hwnd">A top-level window.</param>
    /// <returns>
    /// False for a window on the desktop in front, and when Windows cannot say, as while Explorer restarts:
    /// a window is taken for another desktop's only on Windows' word.
    /// </returns>
    internal static bool KeepsElsewhere(nint hwnd)
    {
        if (hwnd == 0)
        {
            return false;
        }

        // Fetched for each call and let go after it. The shell asks rarely, and a manager kept across an Explorer restart answers nothing.
        var managerClass = ManagerClass;
        var managerInterface = ManagerInterface;
        if (CoCreateInstance(ref managerClass, 0, ClsctxAll, ref managerInterface, out var manager) < 0 || manager == 0)
        {
            return false;
        }

        try
        {
            var here = 0;
            var result = ((delegate* unmanaged[Stdcall]<nint, nint, int*, int>)(*(nint**)manager)[IsOnCurrentSlot])(manager, hwnd, &here);
            return result >= 0 && here == 0;
        }
        finally
        {
            Marshal.Release(manager);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid classId, nint outer, int context, ref Guid interfaceId, out nint instance);
}
