using System.Globalization;

namespace Winland.Domain;

public sealed record WindowAppeared(WindowId Window, Rectangle WorkArea) : Event
{
    public Point? Pointer { get; init; }

    public WindowTraits Traits { get; init; } = WindowTraits.Normal;

    public Rectangle Bounds { get; init; }

    public long ProcessId { get; init; }

    public IReadOnlyList<int> AncestorProcessIds { get; init; } = [];

    /// <summary>The window that owns this one, as a dialog's owner, or null.</summary>
    public WindowId? Owner { get; init; }

    /// <summary>Windows has disabled the window, as it does the owner of an open modal dialog.</summary>
    public bool Disabled { get; init; }

    /// <summary>
    /// The window was maximized when it first showed, as a client that asks to be maximized before it maps. The core
    /// ignores the maximize request that follows.
    /// </summary>
    public bool ShownMaximized { get; init; }

    /// <summary>The full path of the window's program, or empty when it could not be read. The traits' exe is only the file name.</summary>
    public string Executable { get; init; } = "";
}
