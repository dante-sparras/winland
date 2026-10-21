namespace Winland.Domain;

/// <summary>One step of a mouse move or resize. <see cref="KeepRatio"/> holds a floating Client's aspect ratio while it is resized.</summary>
/// <summary>
/// One step of a mouse drag. <paramref name="CenterOnLift"/> is <c>binds:drag_center_window</c>: a tile lifted out of its
/// Layout is held by its middle, or by the point the pointer grabbed, at the same fraction of its size.
/// </summary>
internal readonly record struct PointerDrag(bool Resize, PointerDragPhase Phase, Point Pointer, bool KeepRatio = false, bool CenterOnLift = true);
