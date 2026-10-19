using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace Winland.Windows;

/// <summary>The two ways a window can be given a blurred backdrop.</summary>
internal interface IBackdropSurface
{
    /// <summary>Sets <c>DWMWA_SYSTEMBACKDROP_TYPE</c>. False when the window refuses it.</summary>
    bool TrySetSystemBackdrop(uint type);

    /// <summary>Sets the window composition accent, <c>ACCENT_ENABLE_ACRYLICBLURBEHIND</c> or off. False when the window refuses it.</summary>
    bool TrySetAccent(bool acrylic);
}

/// <summary>
/// Gives one of Winland's own windows a blurred backdrop, as <c>group:groupbar:blur</c> asks. The documented system
/// backdrop (<c>DWMSBT_TRANSIENTWINDOW</c>, the acrylic one) is tried first, and the window composition accent
/// (<c>ACCENT_ENABLE_ACRYLICBLURBEHIND</c>) only when the window refuses it. A window that refuses both stays as it is,
/// without a message. The window's own pixels draw over the backdrop with their alpha.
/// </summary>
internal sealed class BarBackdrop(IBackdropSurface surface)
{
    internal const uint NoBackdrop = 1;
    internal const uint BlurBackdrop = 3;

    private enum Kind
    {
        None,
        System,
        Accent,
        Refused,
    }

    private Kind _current;

    /// <summary>True when the window shows a blurred backdrop now.</summary>
    internal bool Blurred => _current is Kind.System or Kind.Accent;

    /// <summary>Turns the blur on or off. Asking for the state the window is already in does nothing.</summary>
    internal void Set(bool blur)
    {
        if (blur)
        {
            if (_current != Kind.None)
                return;

            _current = surface.TrySetSystemBackdrop(BlurBackdrop) ? Kind.System
                : surface.TrySetAccent(true) ? Kind.Accent
                : Kind.Refused;
            return;
        }

        switch (_current)
        {
            case Kind.System:
                _ = surface.TrySetSystemBackdrop(NoBackdrop);
                break;
            case Kind.Accent:
                _ = surface.TrySetAccent(false);
                break;
        }

        _current = Kind.None;
    }
}

// Stryker disable once all : each member sets a real window's DWM backdrop or accent.
internal sealed partial class Win32BackdropSurface(HWND window) : IBackdropSurface
{
    // DWMWA_SYSTEMBACKDROP_TYPE.
    private const DWMWINDOWATTRIBUTE BackdropAttribute = (DWMWINDOWATTRIBUTE)38;
    private const int AccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentAcrylicBlurBehind = 4;

    // ABGR, almost clear: the bar draws its own colors over the blur.
    private const uint AccentTint = 0x01000000;

    public unsafe bool TrySetSystemBackdrop(uint type) =>
        PInvoke.DwmSetWindowAttribute(window, BackdropAttribute, &type, sizeof(uint)).Succeeded;

    public unsafe bool TrySetAccent(bool acrylic)
    {
        var accent = new AccentData
        {
            State = acrylic ? AccentAcrylicBlurBehind : AccentDisabled,
            Color = acrylic ? AccentTint : 0,
        };
        var data = new CompositionData { Attribute = AccentPolicy, Data = (nint)(&accent), Size = sizeof(AccentData) };
        try
        {
            return SetWindowCompositionAttribute((nint)window.Value, ref data) != 0;
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial int SetWindowCompositionAttribute(nint window, ref CompositionData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentData
    {
        public int State;
        public int Flags;
        public uint Color;
        public int Animation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionData
    {
        public int Attribute;
        public nint Data;
        public int Size;
    }
}
