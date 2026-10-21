using System.Globalization;

namespace Winland.Domain;

/// <summary>
/// A mouse bind already matched on the hook thread. The worker applies the drag under the core lock.
/// </summary>
public sealed record PointerGesture(string Dispatcher, Point Position, PointerDragPhase Phase, string Button) : Command
{
    /// <summary>The bind's argument, such as the aspect ratio mode of <c>resizewindow</c>.</summary>
    public string Arguments { get; init; } = "";
}
