namespace Winland.Domain;

/// <summary>The steps of a <see cref="MouseDrag"/> that depend on how a Layout keeps its tiles.</summary>
internal interface IMouseDragHost
{
    FloatingClients Floating { get; }

    /// <summary><c>general:resize_corner</c>, which forces the grabbed corner of a floating Client.</summary>
    int ResizeCorner { get; }

    /// <summary>The rectangle a drag of this Client starts from. False when it cannot be dragged.</summary>
    bool TryDragBounds(ClientId id, out Rectangle bounds, out bool floating);

    /// <summary>A floating rectangle snapped to the other Clients and the monitor.</summary>
    Rectangle SnapFloating(ClientId self, Rectangle proposed, SnapEdges edges, bool resize);

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
