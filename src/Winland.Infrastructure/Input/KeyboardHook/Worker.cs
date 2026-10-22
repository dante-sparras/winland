using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winland.Infrastructure;

/// <summary>The worker that runs dispatchers off the hook thread, the keys it gives back when a bind fails, and the long press timer.</summary>
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
        var noted = false;
        try
        {
            if (queued.Kind == QueuedKind.Inhibit)
            {
                _core.Handle(new InputInhibited(queued.Inhibited));
                return;
            }

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

            var outcome = _core.Handle(new Dispatch(queued.Dispatcher, queued.Arguments) { Trigger = queued.Trigger });
            Apply(outcome);
            noted = NoteConsumption(queued, outcome.Error is null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);
            if (!noted)
                NoteConsumption(queued, false);
        }
    }

    private static bool NoteConsumption(Queued queued, bool succeeded)
    {
        if (queued.Consumption is not { } note)
            return true;

        note.Note(succeeded);
        return true;
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

    internal void NoteTiming(BindDecision decision)
    {
        if (decision.DisarmLongPress)
            DisarmLongPress();
        if (decision.ArmLongPressMs is int delay)
            ArmLongPress(delay);
    }

    internal void Enqueue(BindDecision decision, SwallowedKey? replay = null)
    {
        BindConsumption? batch = null;
        if (decision.PassesOnFailure && replay is SwallowedKey key)
            batch = new BindConsumption(() => PostReplay(key, decision.ReleaseOnFailure));

        if (decision.Dispatches.Count > 0)
        {
            foreach (var dispatch in decision.Dispatches)
                PostConsumed(dispatch.Dispatcher, dispatch.Arguments, decision.Trigger, batch);

            return;
        }

        if (!string.IsNullOrEmpty(decision.Dispatcher))
            PostConsumed(decision.Dispatcher, decision.Arguments, decision.Trigger, batch);
    }

    private void PostConsumed(string dispatcher, string arguments, BindTrigger? trigger, BindConsumption? batch)
    {
        if (dispatcher.Length == 0)
            return;

        batch?.Expect();
        _queued.Post(new Queued(
            QueuedKind.Dispatch,
            dispatcher,
            arguments,
            false,
            Trigger: trigger,
            Consumption: batch));
    }

    private void PostReplay(SwallowedKey key, IReadOnlyList<string> release, bool? stillDown = null)
    {
        lock (_replayGate)
            _replays.Enqueue((key, release, stillDown));

        Post(ReplayMessage);
    }

    // On the hook thread. Keys caught for the failed bind are let go, then the key is injected from the injector's
    // thread so this hook is not re-entered while it decides.
    private void ReplayPending()
    {
        while (true)
        {
            (SwallowedKey Key, IReadOnlyList<string> Release, bool? StillDown) next;
            lock (_replayGate)
            {
                if (_replays.Count == 0)
                    return;

                next = _replays.Dequeue();
            }

            _session.ForgetCaught(next.Key.Name);
            foreach (var name in next.Release)
                _session.ForgetCaught(name);

            var stillDown = next.StillDown ?? IsDown((int)next.Key.VirtualKey);
            if (next.Key.Pressed && stillDown && next.Key.VirtualKey <= 255)
                _givenBack[next.Key.VirtualKey] = true;

            InputInjector.Post(ReplayInputs(next.Key, stillDown));
        }
    }

    /// <summary>
    /// The input that gives a swallowed key back. A press the user still holds is a press. One they have already
    /// released is a press and a release together. A release edge is the release.
    /// </summary>
    internal static INPUT[] ReplayInputs(SwallowedKey key, bool stillDown)
    {
        if (!key.Pressed)
            return [Key(key.VirtualKey, up: true, key.Scan, key.Extended)];

        var down = Key(key.VirtualKey, up: false, key.Scan, key.Extended);
        if (stillDown)
            return [down];

        return [down, Key(key.VirtualKey, up: true, key.Scan, key.Extended)];
    }

    /// <summary>The drag a mouse bind started, from a mouse button or a key, which pointer moves carry on.</summary>
    internal BindDrag Drag { get; } = new();

    internal void PostDispatch(string dispatcher, string arguments, BindTrigger? trigger = null) =>
        _queued.Post(new Queued(QueuedKind.Dispatch, dispatcher, arguments, false, Trigger: trigger));

    internal void PostGesture(string dispatcher, string arguments, Point position, PointerDragPhase phase, string button) =>
        _queued.Post(new Queued(QueuedKind.Gesture, dispatcher, arguments, false, position, phase, button));

    internal void PostMove(Point position, uint time) =>
        _queued.Post(new Queued(QueuedKind.Move, "", "", false, position, TimeMs: time, HasTime: true));

    internal void PostInput(Input input) =>
        _queued.Post(new Queued(QueuedKind.Input, "", "", false, Event: input));

    internal void CheckLongPress()
    {
        if (_disposed)
            return;

        DisarmLongPress();
        Enqueue(_core.Decide(new LongPressElapsed(), _session));
    }

    private void ArmLongPress(int delay)
    {
        lock (_timerGate)
        {
            _longPressTimer?.Dispose();
            _longPressTimer = new Timer(OnLongPressDue, null, delay, Timeout.Infinite);
        }
    }

    private void DisarmLongPress()
    {
        lock (_timerGate)
        {
            _longPressTimer?.Dispose();
            _longPressTimer = null;
        }
    }

    private void OnLongPressDue(object? _) => Post(LongPressMessage);

    private enum QueuedKind : byte
    {
        Dispatch = 1,
        Inhibit = 2,
        Gesture = 3,
        Move = 4,
        Input = 5,
    }

    private readonly record struct Queued(
        QueuedKind Kind,
        string Dispatcher,
        string Arguments,
        bool Inhibited,
        Point Position = default,
        PointerDragPhase Phase = PointerDragPhase.Begin,
        string Button = "",
        long TimeMs = 0,
        bool HasTime = false,
        Input? Event = null,
        BindTrigger? Trigger = null,
        BindConsumption? Consumption = null);
}
