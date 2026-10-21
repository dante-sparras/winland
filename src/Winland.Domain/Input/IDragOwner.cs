namespace Winland.Domain;

/// <summary>A Layout that runs a <see cref="MouseDrag"/>, so a drag can move with a window onto another Workspace.</summary>
internal interface IDragOwner
{
    MouseDrag Drag { get; }
}
