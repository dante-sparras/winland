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

    private void Pump()
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

    private void Perform(Queued queued)
    {
        try
        {
            if (queued.Kind == QueuedKind.Gesture)
            {
                Apply(_core.Handle(new PointerGesture(queued.Dispatcher, queued.Position, queued.Phase, queued.Button) { Arguments = queued.Arguments }));
                return;
            }

            if (queued.Kind == QueuedKind.Move)
            {
                var moved = new PointerMoved(queued.Position);
                if (queued.HasTime)
                    moved = moved with { TimeMs = queued.TimeMs };

                Apply(_core.Handle(moved));
                return;
            }

            if (queued.Kind == QueuedKind.Input && queued.Event is { } input)
            {
                Apply(_core.Handle(input));
                return;
            }

            Apply(_core.Handle(new Dispatch(queued.Dispatcher, queued.Arguments) { Trigger = queued.Trigger }));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);
        }
    }

    private void StopWorker()
    {
        _queued.Complete();
        _worker?.Join(TimeSpan.FromSeconds(2));
        _worker = null;
    }

    // The adapter asks the window to close, after its close animation when it has one.
    private static void Apply(Outcome outcome) => WindowAdapter.Apply(outcome);

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
