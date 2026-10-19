using System.Globalization;
using KeraLua;

namespace Winland.Lua;

// hl.gesture with a Lua function, or a table of start, update and finish functions, as its action. The gesture line
// names Winland's own luafunction action with a number, and the core reports each phase of a swipe that takes it,
// which the runtime hands here on the Lua thread.
internal sealed partial class LuaRun
{
    private readonly Dictionary<int, GestureCallbacks> _gestureCallbacks = [];
    private readonly List<GestureCall> _heldGestures = [];

    // The fields of an action table, as the pinned release reads them: the end is "finish".
    private static readonly string[] CallbackNames = ["start", "update", "finish"];

    // The functions of one gesture, by their registry references, or 0 for none. A gesture whose action is one
    // function calls it once the fingers lift, with nothing, as in the pinned release.
    private sealed record GestureCallbacks(int Start, int Update, int Finish, bool EndOnly);

    // The action of hl.gesture when it is a function or a table of them: the number the gesture line names, null when
    // the action is neither, or an error.
    private (int? Id, string? Error) KeepGestureAction()
    {
        _lua.GetField(1, "action");
        try
        {
            if (_lua.IsFunction(-1))
            {
                _lua.PushCopy(-1);
                return (AddCallbacks(new GestureCallbacks(0, 0, _lua.Ref(LuaRegistry.Index), EndOnly: true)), null);
            }

            if (!_lua.IsTable(-1))
                return (null, null);

            var table = _lua.GetTop();
            var references = new List<int>(3);
            foreach (var name in CallbackNames)
            {
                _lua.GetField(table, name);
                if (_lua.IsNil(-1))
                {
                    _lua.Pop(1);
                    references.Add(0);
                    continue;
                }

                if (!_lua.IsFunction(-1))
                {
                    _lua.Pop(1);
                    Release(references);
                    return (null, $"hl.gesture: action.{name} must be a function");
                }

                references.Add(_lua.Ref(LuaRegistry.Index));
            }

            if (references.All(reference => reference == 0))
                return (null, "hl.gesture: action callback table must define at least one of start, update, end, or finish");

            return (AddCallbacks(new GestureCallbacks(references[0], references[1], references[2], EndOnly: false)), null);
        }
        finally
        {
            _lua.Pop(1);
        }
    }

    private int AddCallbacks(GestureCallbacks callbacks)
    {
        var id = ++_nextFunction;
        _gestureCallbacks[id] = callbacks;
        return id;
    }

    // Lets go of the functions of a gesture that will not be made.
    private void DropCallbacks(int id)
    {
        if (_gestureCallbacks.Remove(id, out var callbacks))
            Release([callbacks.Start, callbacks.Update, callbacks.Finish]);
    }

    private void Release(IEnumerable<int> references)
    {
        foreach (var reference in references)
        {
            if (reference != 0)
                _lua.Unref(LuaRegistry.Index, reference);
        }
    }

    /// <summary>
    /// Keeps the phases of a swipe that a command of this Lua made, to run once that command has returned, as the
    /// pinned release runs them inside the call that made the swipe.
    /// </summary>
    public void Hold(IReadOnlyList<GestureCall> calls) => _heldGestures.AddRange(calls);

    // Runs a command as the pipe does, and then the gesture callbacks of a swipe it made.
    private string Command(string command)
    {
        var reply = _world?.Command(command) ?? "error: winland is not running yet";
        RunHeldGestures();
        return reply;
    }

    /// <summary>Runs the gesture callbacks of the swipes the Lua's own commands made.</summary>
    public void RunHeldGestures()
    {
        while (_heldGestures.Count > 0)
        {
            var call = _heldGestures[0];
            _heldGestures.RemoveAt(0);
            Gesture(call);
        }
    }

    /// <summary>Hands one phase of a swipe to the functions of the gesture it took.</summary>
    public void Gesture(GestureCall call)
    {
        if (!int.TryParse(call.Function, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            || !_gestureCallbacks.TryGetValue(id, out var callbacks))
        {
            return;
        }

        if (callbacks.EndOnly)
        {
            if (call.Phase != GesturePhase.End)
                return;

            _lua.RawGetInteger(LuaRegistry.Index, callbacks.Finish);
            Guarded(BindLimit, "keybind callback");
            return;
        }

        var (reference, label) = call.Phase switch
        {
            GesturePhase.Start => (callbacks.Start, "gesture start callback"),
            GesturePhase.Update => (callbacks.Update, "gesture update callback"),
            _ => (callbacks.Finish, "gesture end callback"),
        };
        if (reference == 0)
            return;

        _lua.RawGetInteger(LuaRegistry.Index, reference);
        PushGestureEvent(call);
        Guarded(EventLimit, label, arguments: 1);
    }

    // The event a gesture callback is given: its phase, the direction, and for a start or an update the fingers and
    // the delta of the update, or for an end whether it was cancelled. A swipe is the only type the core reports.
    private void PushGestureEvent(GestureCall call)
    {
        _lua.NewTable();
        _lua.PushString(call.Phase switch
        {
            GesturePhase.Start => "start",
            GesturePhase.Update => "update",
            _ => "end",
        });
        _lua.SetField(-2, "phase");
        _lua.PushString(DirectionName(call.Direction));
        _lua.SetField(-2, "direction");
        _lua.PushString("swipe");
        _lua.SetField(-2, "type");
        _lua.PushInteger(call.TimeMs);
        _lua.SetField(-2, "time_ms");
        if (call.Phase == GesturePhase.End)
        {
            _lua.PushBoolean(call.Cancelled);
            _lua.SetField(-2, "cancelled");
            return;
        }

        _lua.PushInteger(call.Fingers);
        _lua.SetField(-2, "fingers");
        _lua.NewTable();
        _lua.PushNumber(call.DeltaX);
        _lua.SetField(-2, "x");
        _lua.PushNumber(call.DeltaY);
        _lua.SetField(-2, "y");
        _lua.SetField(-2, "delta");
    }

    private static string DirectionName(GestureDirection direction) => direction switch
    {
        GestureDirection.PinchIn => "PINCH_IN",
        GestureDirection.PinchOut => "PINCH_OUT",
        _ => direction.ToString().ToUpperInvariant(),
    };
}
