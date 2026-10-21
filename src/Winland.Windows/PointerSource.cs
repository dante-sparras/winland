using Winland.Core;

namespace Winland.Windows;

/// <summary>What moved the pointer or pressed its button, as the low-level mouse hook sees it.</summary>
public enum PointerSource
{
    Mouse,

    /// <summary>Another program sent the input.</summary>
    Injected,

    /// <summary>Windows made the input from a touch screen.</summary>
    Touch,

    /// <summary>Windows made the input from a pen.</summary>
    Pen,
}

public static class PointerSources
{
    // Windows marks the mouse input it makes from touch and pens with this signature, and touch with one more bit.
    private const ulong PenSignature = 0xFF515700;
    private const ulong SignatureMask = 0xFFFFFF00;
    private const ulong TouchBit = 0x80;
    private const uint InjectedFlag = 0x1;

    /// <summary>The source of a hook event, from its flags and extra information.</summary>
    public static PointerSource Of(uint flags, ulong extra)
    {
        if ((extra & SignatureMask) == PenSignature)
            return (extra & TouchBit) != 0 ? PointerSource.Touch : PointerSource.Pen;

        return (flags & InjectedFlag) != 0 ? PointerSource.Injected : PointerSource.Mouse;
    }
}
