using System.Globalization;
using System.Runtime.InteropServices;
using Winland.Core;
using Winland.Core.Diagnostics;

namespace Winland.Platform;

/// <summary>
/// Explorer's application-view cloak: <c>IApplicationView::SetCloak</c>, reached through the immersive shell's view collection (ADR 0059).
/// It takes another program's window off the screen from an ordinary process, where <c>DWMWA_CLOAK</c> is refused.
/// The window then reads <c>DWM_CLOAKED_SHELL</c>, as a window on another virtual desktop does, and any process can take the cloak off.
/// Explorer is the door to the cloak and does not hold it: a cloaked window stays cloaked while Explorer restarts,
/// and what was fetched from the Explorer that ended answers <c>0x800706BA</c>.
/// The interfaces are not documented. Their identifiers and slots are the ones komorebi and GlazeWM call.
/// </summary>
internal static unsafe class ShellCloak
{
    private static readonly Guid ImmersiveShell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid ServiceProvider = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid ViewCollection = new("1841C6D7-4F9D-42C0-AF41-8747538F10E5");
    private const int ClsctxLocalServer = 0x4;
    private const int CoinitMultithreaded = 0;

    // IServiceProvider::QueryService, after the three slots of IUnknown.
    private const int QueryServiceSlot = 3;

    // IApplicationViewCollection::GetViewForHwnd, after GetViews, GetViewsByZOrder, and GetViewsByAppUserModelId.
    private const int GetViewForHwndSlot = 6;

    // IApplicationView::SetCloak, after the three slots of IInspectable and SetFocus, SwitchTo, TryInvokeBack,
    // GetThumbnailWindow, GetMonitor, and GetVisibility.
    private const int SetCloakSlot = 12;

    // The cloak kind every caller of SetCloak passes, and the flag that puts it on. Zero takes it off.
    private const int CloakKind = 1;
    private const int CloakOn = 2;

    // The apartment kinds CoGetApartmentType answers for a single-threaded apartment.
    private const int ApartmentSta = 0;
    private const int ApartmentMainSta = 3;

    private static readonly object Gate = new();

    // The view collection, kept between calls. A proxy belongs to the apartment that fetched it: a single-threaded apartment
    // keeps its own, and every other thread shares one. A view is never kept. It is fetched for each call and let go after it.
    [ThreadStatic]
    private static nint _own;
    private static nint _shared;

    /// <summary>Puts Explorer's cloak on <paramref name="hwnd"/>, or takes it off.</summary>
    /// <param name="hwnd">A top-level window of any process.</param>
    /// <param name="cloak">True to cloak the window. False to take Winoma's cloak off it.</param>
    /// <returns>
    /// <see cref="CloakRefusal.None"/> when Explorer did it. A failure through a collection kept from an earlier call drops it,
    /// reaches Explorer again, and asks once more, which is what an Explorer restart needs.
    /// </returns>
    internal static CloakRefusal Set(nint hwnd, bool cloak)
    {
        if (hwnd == 0)
        {
            return CloakRefusal.NoView;
        }

        var refusal = Ask(hwnd, cloak, out var kept, out var result);
        if (refusal != CloakRefusal.None && kept)
        {
            refusal = Ask(hwnd, cloak, out _, out result);
        }

        if (refusal != CloakRefusal.None)
        {
            Log.Information(
                "Shell",
                "Explorer answered 0x" + result.ToString("X8", CultureInfo.InvariantCulture) + " for the " + (cloak ? "cloak" : "uncloak") + " of window " + hwnd + ": " + refusal);
        }

        return refusal;
    }

    /// <summary>One try. <paramref name="kept"/> is true when the collection came from an earlier call, so a failure may only mean it went stale.</summary>
    private static CloakRefusal Ask(nint hwnd, bool cloak, out bool kept, out int result)
    {
        var views = Hold(out kept, out result);
        if (views == 0)
        {
            return CloakRefusal.Unreachable;
        }

        try
        {
            nint view = 0;
            result = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(views, GetViewForHwndSlot))(views, hwnd, &view);
            if (result < 0 || view == 0)
            {
                Forget(views, kept);
                return CloakRefusal.NoView;
            }

            try
            {
                result = ((delegate* unmanaged[Stdcall]<nint, int, int, int>)Slot(view, SetCloakSlot))(view, CloakKind, cloak ? CloakOn : 0);
            }
            finally
            {
                Marshal.Release(view);
            }

            if (result < 0)
            {
                Forget(views, kept);
                return CloakRefusal.Refused;
            }

            return CloakRefusal.None;
        }
        finally
        {
            Marshal.Release(views);
        }
    }

    /// <summary>The kept collection with a reference of the caller's own, reaching Explorer when none is kept. Zero when Explorer cannot be reached.</summary>
    private static nint Hold(out bool kept, out int result)
    {
        var single = SingleThreaded();
        result = 0;
        lock (Gate)
        {
            var views = single ? _own : _shared;
            if (views != 0)
            {
                Marshal.AddRef(views);
                kept = true;
                return views;
            }
        }

        kept = false;
        var reached = Reach(out result);
        if (reached == 0)
        {
            return 0;
        }

        lock (Gate)
        {
            // Another thread of the shared apartment may have reached Explorer meanwhile. Its collection is as good, and this one is let go.
            var views = single ? _own : _shared;
            if (views != 0)
            {
                Marshal.Release(reached);
                Marshal.AddRef(views);
                return views;
            }

            if (single)
            {
                _own = reached;
            }
            else
            {
                _shared = reached;
            }

            Marshal.AddRef(reached);
            return reached;
        }
    }

    /// <summary>
    /// Drops <paramref name="views"/> after a failure, when it came from an earlier call and is still the kept collection,
    /// so the next call reaches Explorer again. One fetched for this call is kept: the failure is the window's.
    /// </summary>
    private static void Forget(nint views, bool kept)
    {
        if (!kept)
        {
            return;
        }

        lock (Gate)
        {
            if (_own == views)
            {
                _own = 0;
            }
            else if (_shared == views)
            {
                _shared = 0;
            }
            else
            {
                return;
            }
        }

        Marshal.Release(views);
    }

    /// <summary>Explorer's view collection, fetched now. Zero when Explorer is not there to answer.</summary>
    private static nint Reach(out int result)
    {
        var shellClass = ImmersiveShell;
        var providerInterface = ServiceProvider;
        result = CoCreateInstance(ref shellClass, 0, ClsctxLocalServer, ref providerInterface, out var provider);
        if (result < 0 || provider == 0)
        {
            return 0;
        }

        try
        {
            var service = ViewCollection;
            nint views = 0;
            result = ((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, nint*, int>)Slot(provider, QueryServiceSlot))(provider, &service, &service, &views);
            return result < 0 ? 0 : views;
        }
        finally
        {
            Marshal.Release(provider);
        }
    }

    /// <summary>True on a thread in a single-threaded apartment. A thread in none joins the shared one, as the thread pool's threads are.</summary>
    private static bool SingleThreaded()
    {
        if (CoGetApartmentType(out var kind, out _) >= 0)
        {
            return kind is ApartmentSta or ApartmentMainSta;
        }

        _ = CoInitializeEx(0, CoinitMultithreaded);
        return false;
    }

    private static nint Slot(nint target, int index) => (*(nint**)target)[index];

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, int model);

    [DllImport("ole32.dll")]
    private static extern int CoGetApartmentType(out int kind, out int qualifier);

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid classId, nint outer, int context, ref Guid interfaceId, out nint instance);
}
