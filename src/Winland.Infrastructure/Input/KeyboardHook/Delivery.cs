namespace Winland.Infrastructure;

/// <summary>How the hook decides a key edge.</summary>
public sealed partial class KeyboardHook : IDisposable
{
    /// <summary>The hook's decision for one key edge, and whether the edge reaches the windows.</summary>
    internal (bool Passes, BindDecision Decision) Deliver(KeyEdge edge)
    {
        Input input = edge.Up
            ? new KeyReleased(edge.Name) { ScanCode = edge.Xkb }
            : new KeyPressed(edge.Name) { ScanCode = edge.Xkb };

        // The mouse dispatcher bound to a key drags while the key is held: its repeats are swallowed, and its release
        // drops the window.
        var draggedByKey = Drag.Current is BindDrag.Active held && held.Button.Equals(edge.Name, StringComparison.OrdinalIgnoreCase);
        if (draggedByKey && !edge.Up)
            return (false, new BindDecision(true, null));
        if (draggedByKey && Drag.Release(edge.Name) is BindDrag.Active dropped)
            PostGesture(dropped.Dispatcher, dropped.Arguments, CursorPoint(), PointerDragPhase.End, edge.Name);

        var decision = _core.Decide(input, _session);

        // With no window under the pointer a key whose bind only drags goes on to the focused window, as the pinned
        // release lets the event through.
        var keyDrag = edge.Up ? null : KeyDrag(decision);
        // Its repeats and release go on too, so the window never sees the key stuck down.
        if (keyDrag is not null && Drag.Current is null && MouseHook.OnlyDrag(decision) && !_core.HasDragTarget(CursorPoint()))
        {
            _session.ForgetCaught(edge.Name);
            return (true, decision);
        }

        if (keyDrag is BindDispatch drag && Drag.Begin(edge.Name, drag.Dispatcher, drag.Arguments))
        {
            PostGesture(drag.Dispatcher, drag.Arguments, CursorPoint(), PointerDragPhase.Begin, edge.Name);
            foreach (var dispatch in decision.Dispatches)
            {
                if (dispatch.Dispatcher.Length > 0 && !dispatch.Drag)
                    PostDispatch(dispatch.Dispatcher, dispatch.Arguments, decision.Trigger);
            }

            return (false, decision);
        }

        Enqueue(decision);
        if (draggedByKey)
            return (false, decision);

        // Sent now, while Super or Alt is still down, so it lands before their release.
        if (decision.Mask)
            Mask();

        return (!decision.Consumed, decision);
    }
}
