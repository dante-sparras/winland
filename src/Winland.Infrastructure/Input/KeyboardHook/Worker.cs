using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winland.Infrastructure;

/// <summary>The worker that runs dispatchers off the hook thread.</summary>
public sealed partial class KeyboardHook : IDisposable
{
    private void Post(uint message)
    {
        if (_disposed || _hookThread == 0)
            return;

        _ = PInvoke.PostThreadMessage(_hookThread, message, default, default);
    }

    internal void Pump()
    {
        while (_queued.Take() is { Count: > 0 } batch)
        {
            foreach (var queued in batch)
                Perform(queued);
        }
    }

    // A pointer move, or a move of the same drag, makes the one still waiting pointless.
    private static bool Replaces(Queued waiting, Queued next) => (waiting.Kind, next.Kind) switch
    {
        (QueuedKind.Move, QueuedKind.Move) => true,
        (QueuedKind.Gesture, QueuedKind.Gesture) =>
            waiting.Phase == PointerDragPhase.Move
            && next.Phase == PointerDragPhase.Move
            && waiting.Button == next.Button
            && waiting.Dispatcher == next.Dispatcher
            && waiting.Arguments == next.Arguments,
        _ => false,
    };

    // Steps of one touchpad swipe add up, so a burst of them while the worker is busy is one step and not a backlog
    // that leaves the slide behind the fingers.
    private static (bool, Queued) Join(Queued waiting, Queued next)
    {
        if (waiting.Kind != QueuedKind.Input || next.Kind != QueuedKind.Input
            || waiting.Event is not SwipeUpdated first || next.Event is not SwipeUpdated second
            || first.Fingers != second.Fingers)
            return (false, next);

        var joined = second with
        {
            DeltaX = first.DeltaX + second.DeltaX,
            DeltaY = first.DeltaY + second.DeltaY,
            Steps = first.Steps + second.Steps,
        };
        return (true, next with { Event = joined });
    }

    private void Perform(Queued queued)
    {
        try
        {
            if (queued.Kind == QueuedKind.Gesture)
            {
                _apply(_core.Handle(new PointerGesture(queued.Dispatcher, queued.Position, queued.Phase, queued.Button) { Arguments = queued.Arguments }));
                return;
            }

            if (queued.Kind == QueuedKind.Move)
            {
                var moved = new PointerMoved(queued.Position) { Under = TopLevelUnder(queued.Position) };
                if (queued.HasTime)
                    moved = moved with { TimeMs = queued.TimeMs };

                _apply(_core.Handle(moved));
                return;
            }

            if (queued.Kind == QueuedKind.Input && queued.Event is { } input)
            {
                _apply(_core.Handle(input));
                return;
            }

            _apply(_core.Handle(new Dispatch(queued.Dispatcher, queued.Arguments) { Trigger = queued.Trigger }));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception);
        }
    }

    // Read here, off the hook thread. Winland's own windows, such as a Group's tab bar, are not another program's, and
    // the desktop shows through a tile's cut corners.
    private static WindowId? TopLevelUnder(Point position)
    {
        var under = PInvoke.WindowFromPoint(new System.Drawing.Point(position.X, position.Y));
        if (under.IsNull)
            return null;

        var root = PInvoke.GetAncestor(under, Windows.Win32.UI.WindowsAndMessaging.GET_ANCESTOR_FLAGS.GA_ROOT);
        if (root.IsNull || root == PInvoke.GetShellWindow() || WindowAdapter.ClassName(root) == "WorkerW")
            return null;

        _ = PInvoke.GetWindowThreadProcessId(root, out var process);
        return process == (uint)Environment.ProcessId ? null : new WindowId(((nint)root).ToInt64());
    }

    private void StopWorker()
    {
        _queued.Complete();
        _worker?.Join(TimeSpan.FromSeconds(2));
        _worker = null;
    }

    private LRESULT Next(int code, WPARAM wParam, LPARAM lParam) =>
        PInvoke.CallNextHookEx(_hook, code, wParam, lParam);

    internal void Enqueue(BindDecision decision)
    {
        if (decision.Dispatches.Count > 0)
        {
            foreach (var dispatch in decision.Dispatches)
                PostDispatch(dispatch.Dispatcher, dispatch.Arguments, decision.Trigger);

            return;
        }

        if (!string.IsNullOrEmpty(decision.Dispatcher))
            PostDispatch(decision.Dispatcher, decision.Arguments, decision.Trigger);
    }

    /// <summary>The drag a mouse bind started, from a mouse button or a key, which pointer moves carry on.</summary>
    internal BindDrag Drag { get; } = new();

    internal void PostDispatch(string dispatcher, string arguments, BindTrigger? trigger = null)
    {
        if (dispatcher.Length > 0)
            _queued.Post(new Queued(QueuedKind.Dispatch, dispatcher, arguments, Trigger: trigger));
    }

    internal void PostGesture(string dispatcher, string arguments, Point position, PointerDragPhase phase, string button) =>
        _queued.Post(new Queued(QueuedKind.Gesture, dispatcher, arguments, position, phase, button));

    internal void PostMove(Point position, uint time) =>
        _queued.Post(new Queued(QueuedKind.Move, "", "", position, TimeMs: time, HasTime: true));

    internal void PostInput(Input input) =>
        _queued.Post(new Queued(QueuedKind.Input, "", "", Event: input));

    private enum QueuedKind : byte
    {
        Dispatch = 1,
        Gesture = 3,
        Move = 4,
        Input = 5,
    }

    private readonly record struct Queued(
        QueuedKind Kind,
        string Dispatcher,
        string Arguments,
        Point Position = default,
        PointerDragPhase Phase = PointerDragPhase.Begin,
        string Button = "",
        long TimeMs = 0,
        bool HasTime = false,
        Input? Event = null,
        BindTrigger? Trigger = null);
}
