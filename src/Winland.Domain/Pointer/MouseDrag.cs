namespace Winland.Domain;

/// <summary>
/// One mouse move or resize in a Layout. A floating Client follows the pointer, or resizes from the
/// grabbed corner, and snaps as it goes. A tiled Client is lifted out of the Layout on its first move
/// and dropped back on release, or resized through the Layout.
/// </summary>
internal sealed class MouseDrag
{
    private Grab? _grab;

    public IReadOnlyList<Placement> Step(IMouseDragHost host, WindowId window, PointerDrag drag)
    {
        ArgumentNullException.ThrowIfNull(host);
        return drag.Phase switch
        {
            PointerDragPhase.Begin => Begin(host, new ClientId(window.Value), drag),
            PointerDragPhase.Move => Move(host, drag.Pointer, drag.Resize),
            PointerDragPhase.End => End(host, drag.Pointer),
            _ => [],
        };
    }

    /// <summary>Ends the drag of a Client that leaves the Layout. True when it was being dragged.</summary>
    public bool Forget(ClientId id)
    {
        if (_grab is not { } grab || grab.Id != id)
            return false;

        _grab = null;
        return true;
    }

    /// <summary>
    /// Ends the drag here and begins it on <paramref name="next"/>, keeping the grab offset, so the window keeps
    /// following the pointer after it joins another Workspace's Layout.
    /// </summary>
    public void HandTo(MouseDrag next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (ReferenceEquals(this, next) || _grab is not { } grab)
            return;

        _grab = null;
        next._grab = grab;
    }

    private IReadOnlyList<Placement> Begin(IMouseDragHost host, ClientId id, PointerDrag drag)
    {
        if (!host.TryDragBounds(id, out var bounds, out var floating))
        {
            _grab = null;
            return [];
        }

        var (left, top) = BorderPointer.GrabbedCorner(drag.Pointer, bounds);
        _grab = new Grab(id, drag.Resize, floating, drag.Pointer, bounds, left, top, drag.KeepRatio);
        return [];
    }

    private IReadOnlyList<Placement> Move(IMouseDragHost host, Point pointer, bool resize)
    {
        if (_grab is not Grab grab || grab.Resize != resize)
            return [];

        // A Client tiled again while its floating drag runs is left to its Layout.
        var floating = host.Floating;
        if (grab.WasFloating && !floating.Contains(grab.Id))
            return [];

        var dx = pointer.X - grab.Last.X;
        var dy = pointer.Y - grab.Last.Y;
        grab.Last = pointer;
        if (resize)
        {
            if (!grab.WasFloating)
                return host.ResizeTile(grab.Id, dx, dy, (grab.Left, grab.Top));

            var resized = BorderPointer.Resize(grab.Start, grab.Pressed, pointer, grab.Left, grab.Top, grab.KeepRatio);
            floating.Set(grab.Id, host.FloatingLimits(grab.Id).Clamp(resized));
            return Dragged(host.ShowFloating(grab.Id), grab.Id);
        }

        var lifting = !grab.WasFloating && !grab.Lifted;
        if (lifting)
        {
            if (!host.LiftTile(grab.Id, out var box))
                return [];

            grab.Lifted = true;
            grab.Start = box;
            grab.HotX = box.Width / 2;
            grab.HotY = box.Height / 2;
        }

        // The step that lifts a tile lays the others out without it. After that only the dragged Client moves.
        var next = new Rectangle(pointer.X - grab.HotX, pointer.Y - grab.HotY, grab.Start.Width, grab.Start.Height);
        floating.Set(grab.Id, next);
        var shown = host.ShowFloating(grab.Id);
        return lifting ? shown : Dragged(shown, grab.Id);
    }

    private static IReadOnlyList<Placement> Dragged(IReadOnlyList<Placement> placements, ClientId id) =>
        [.. placements.Where(placement => placement.Client == id)];

    private IReadOnlyList<Placement> End(IMouseDragHost host, Point pointer)
    {
        if (_grab is not Grab grab)
            return [];

        _grab = null;
        if (grab.Lifted && host.Floating.Contains(grab.Id))
            return host.DropTile(grab.Id, pointer);

        if (grab.WasFloating && host.Floating.Contains(grab.Id))
            return host.ShowFloating(grab.Id);

        return host.EndInPlace();
    }

    /// <summary>
    /// Where a drag started and where it is. A floating Client keeps the pointer's offset into it.
    /// A lifted tile is held by its middle.
    /// </summary>
    private sealed class Grab(ClientId id, bool resize, bool wasFloating, Point grab, Rectangle start, bool left, bool top, bool keepRatio)
    {
        public ClientId Id { get; } = id;

        public bool Resize { get; } = resize;

        public bool WasFloating { get; } = wasFloating;

        public Point Pressed { get; } = grab;

        public Point Last { get; set; } = grab;

        public Rectangle Start { get; set; } = start;

        public bool Left { get; } = left;

        public bool Top { get; } = top;

        public bool KeepRatio { get; } = keepRatio;

        public bool Lifted { get; set; }

        public int HotX { get; set; } = grab.X - start.X;

        public int HotY { get; set; } = grab.Y - start.Y;
    }
}

/// <summary>The steps of a <see cref="MouseDrag"/> that depend on how a Layout keeps its tiles.</summary>
internal interface IMouseDragHost
{
    FloatingClients Floating { get; }

    SizeLimits FloatingLimits(ClientId id);

    /// <summary>The rectangle a drag of this Client starts from. False when it cannot be dragged.</summary>
    bool TryDragBounds(ClientId id, out Rectangle bounds, out bool floating);

    /// <summary>Takes a tile out of the Layout and floats it at its tiled rectangle. False when it is not tiled.</summary>
    bool LiftTile(ClientId id, out Rectangle box);

    /// <summary>Resizes a tile by the pointer's movement since the last step.</summary>
    IReadOnlyList<Placement> ResizeTile(ClientId id, int dx, int dy, (bool Left, bool Top) corner);

    /// <summary>Puts a lifted tile back into the Layout at the pointer.</summary>
    IReadOnlyList<Placement> DropTile(ClientId id, Point pointer);

    /// <summary>The placements after a floating Client moved.</summary>
    IReadOnlyList<Placement> ShowFloating(ClientId id);

    /// <summary>The placements when a drag ends without a floating Client or a lifted tile.</summary>
    IReadOnlyList<Placement> EndInPlace();
}

/// <summary>A Layout that runs a <see cref="MouseDrag"/>, so a drag can move with a window onto another Workspace.</summary>
internal interface IDragOwner
{
    MouseDrag Drag { get; }
}

/// <summary>One step of a mouse move or resize. <see cref="KeepRatio"/> holds a floating Client's aspect ratio while it is resized.</summary>
internal readonly record struct PointerDrag(bool Resize, PointerDragPhase Phase, Point Pointer, bool KeepRatio = false);

public enum PointerDragPhase
{
    Begin,
    Move,
    End,
}
