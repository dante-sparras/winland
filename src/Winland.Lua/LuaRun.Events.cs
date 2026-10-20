using System.Collections.Immutable;
using System.Globalization;
using KeraLua;

namespace Winland.Lua;

/// <summary>
/// The handlers an event a handler's own dispatch raised came from. That event runs after the handler, not inside it,
/// and is not handed to those handlers again, as the pinned release suppresses a handler's recursive call.
/// </summary>
internal sealed record EventCause(LuaRun Owner, ImmutableArray<long> Handles);

// hl.on: the handlers of each event, called on the runtime's thread with the event's arguments when Winland notices it.
internal sealed partial class LuaRun
{
    internal const double EventLimit = 0.05;

    // How deep handlers may raise events for other handlers, as in the pinned release.
    internal const int MaxDispatchDepth = 32;

    // How many windows and workspaces the focus history keeps.
    private const int HistoryLimit = 256;

    private readonly Dictionary<string, List<long>> _callbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<long, (string Event, int Reference)> _subscriptions = [];
    private readonly HashSet<long> _recursionWarned = [];
    private readonly List<string> _focusHistory = [];
    private readonly List<string> _urgent = [];
    private readonly List<string> _workspaceHistory = [];
    private long _nextHandle;
    private EventCause? _cause;

    /// <summary>The handlers running now, while one runs: an event noticed meanwhile came from them.</summary>
    public EventCause? Cause => Volatile.Read(ref _cause);

    // An argument an event hands its handlers.
    private abstract record EventArgument;

    private sealed record WindowEventArgument(WindowId? Window) : EventArgument;

    private sealed record WorkspaceEventArgument(WorkspaceId? Workspace) : EventArgument;

    private sealed record MonitorEventArgument(string Name) : EventArgument;

    private sealed record NumberEventArgument(long Value) : EventArgument;

    private sealed record TextEventArgument(string Value) : EventArgument;

    private sealed record BooleanEventArgument(bool Value) : EventArgument;

    private long Subscribe(string name, int reference)
    {
        var handle = ++_nextHandle;
        if (!_callbacks.TryGetValue(name, out var handles))
            _callbacks[name] = handles = [];

        handles.Add(handle);
        _subscriptions[handle] = (name, reference);
        return handle;
    }

    // subscription:remove().
    private int Unsubscribe(IntPtr state)
    {
        var handle = _lua.IsInteger(1) ? _lua.ToInteger(1) : 0;
        if (!_subscriptions.Remove(handle, out var subscription))
            return 0;

        _lua.Unref(LuaRegistry.Index, subscription.Reference);
        if (_callbacks.TryGetValue(subscription.Event, out var handles))
            handles.Remove(handle);

        _recursionWarned.Remove(handle);
        return 0;
    }

    /// <summary>Hands the handlers the events of one outcome, in order, and notes what the queries read from them.</summary>
    public void Deliver(IReadOnlyList<StreamEvent> events, EventCause? cause)
    {
        if (_phase != Phase.Running)
            return;

        if (cause is not null && cause.Owner != this)
            cause = null;

        foreach (var streamEvent in events)
        {
            Track(streamEvent);
            foreach (var (name, arguments) in Translate(streamEvent))
                Notify(name, arguments, cause);
        }
    }

    // The events of the pinned release an event of Winland is, with their arguments. Winland's events with no such
    // event give none.
    private static IEnumerable<(string Name, EventArgument[] Arguments)> Translate(StreamEvent streamEvent)
    {
        switch (streamEvent)
        {
            case ClientOpened opened:
                // The pinned release raises open_early as the window maps, before its rules; Winland raises both after.
                yield return ("window.open_early", [new WindowEventArgument(opened.Window)]);
                yield return ("window.open", [new WindowEventArgument(opened.Window)]);
                break;
            case ClientClosed closed:
                yield return ("window.close", [new WindowEventArgument(closed.Window)]);
                yield return ("window.destroy", [new WindowEventArgument(closed.Window)]);
                break;
            case ClientRetitled retitled:
                yield return ("window.title", [new WindowEventArgument(retitled.Window)]);
                break;
            case ClientReclassed reclassed:
                yield return ("window.class", [new WindowEventArgument(reclassed.Window)]);
                break;
            case ClientRulesUpdated updated:
                yield return ("window.update_rules", [new WindowEventArgument(updated.Window)]);
                break;
            case ClientKilled killed:
                yield return ("window.kill", [new WindowEventArgument(killed.Window)]);
                break;
            case FocusChanged focused:
                // Winland does not know why the focus moved: the reason is 0, unknown.
                yield return ("window.active", [new WindowEventArgument(focused.Window), new NumberEventArgument(0)]);
                break;
            case ClientUrgent urgent:
                yield return ("window.urgent", [new WindowEventArgument(urgent.Window)]);
                break;
            case ClientPinned pinned:
                yield return ("window.pin", [new WindowEventArgument(pinned.Window)]);
                break;
            case ClientMinimized minimized:
                yield return ("window.minimize", [new WindowEventArgument(minimized.Window), new BooleanEventArgument(minimized.Minimized)]);
                break;
            case FullscreenChanged fullscreen:
                yield return ("window.fullscreen", [new WindowEventArgument(fullscreen.Window)]);
                break;
            case ClientMoved moved:
                yield return ("window.move_to_workspace", [new WindowEventArgument(moved.Window), new WorkspaceEventArgument(moved.Workspace)]);
                break;
            case MonitorAddedV2 added:
                yield return ("monitor.added", [new MonitorEventArgument(added.Name)]);
                yield return ("monitor.layout_changed", []);
                break;
            case MonitorRemovedV2 removed:
                yield return ("monitor.removed", [new MonitorEventArgument(removed.Name)]);
                yield return ("monitor.layout_changed", []);
                break;
            case MonitorFocused focusedMonitor:
                yield return ("monitor.focused", [new MonitorEventArgument(focusedMonitor.Name)]);
                break;
            case WorkspaceActivated activated:
                yield return ("workspace.active", [new WorkspaceEventArgument(activated.Workspace)]);
                break;
            case ActiveSpecial special:
                yield return ("workspace.special_active", [new WorkspaceEventArgument(special.Workspace), new MonitorEventArgument(special.Monitor)]);
                break;
            case WorkspaceCreated created:
                yield return ("workspace.created", [new WorkspaceEventArgument(created.Workspace)]);
                break;
            case WorkspaceDestroyed destroyed:
                yield return ("workspace.removed", [new WorkspaceEventArgument(destroyed.Workspace)]);
                break;
            case WorkspaceMoved movedWorkspace:
                yield return ("workspace.move_to_monitor", [new WorkspaceEventArgument(movedWorkspace.Workspace), new MonitorEventArgument(movedWorkspace.Monitor)]);
                break;
            case ConfigReloaded:
                // A reload refreshes every prop once it has read the config, as the pinned release schedules it.
                yield return ("config.reloaded", []);
                yield return ("config.props_refreshed", [new BooleanEventArgument(true)]);
                break;
            case SubmapChanged submap:
                yield return ("keybinds.submap", [new TextEventArgument(submap.Name)]);
                break;
        }
    }

    // hl.config and the other calls that change the config while Winland runs schedule one prop refresh, which runs
    // when the outermost Lua call returns, as the pinned release runs it at the end of the current event, or at once
    // through hl.exec_scheduled_prop_refresh_immediately. Its handlers learn which.
    private void ScheduleRefresh()
    {
        if (!_refreshing)
            _refreshScheduled = true;
    }

    private void RunScheduledRefresh(bool scheduled)
    {
        if (!_refreshScheduled)
            return;

        _refreshScheduled = false;
        _refreshing = true;
        try
        {
            Notify("config.props_refreshed", [new BooleanEventArgument(scheduled)], null);
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>A key the user pressed or let go: its XKB keycode, the time in milliseconds and 1 pressed or 0 released.</summary>
    public void Key(int code, uint time, bool pressed)
    {
        if (_phase == Phase.Running)
            Notify("input.keyboard.key", [new NumberEventArgument(code), new NumberEventArgument(time), new NumberEventArgument(pressed ? 1 : 0)], null);
    }

    /// <summary>
    /// The config's Lua is about to be let go, for a reload or because Winland exits: its <c>config.unload</c>
    /// handlers run, as in the pinned release.
    /// </summary>
    public void Unload()
    {
        if (_phase == Phase.Running)
            Notify("config.unload", [], null);
    }

    // Calls each handler of the event in the order they were added, each with the time limit. A handler's error is
    // logged and the others still run.
    private void Notify(string name, EventArgument[] arguments, EventCause? cause)
    {
        if (!_callbacks.TryGetValue(name, out var handles) || handles.Count == 0)
            return;

        var chain = cause?.Handles ?? [];
        if (chain.Length >= MaxDispatchDepth)
        {
            _world?.Log($"lua: max dispatch depth ({MaxDispatchDepth}) reached while handling '{name}'");
            return;
        }

        foreach (var handle in handles.ToArray())
        {
            if (!_subscriptions.TryGetValue(handle, out var subscription))
                continue;

            if (chain.Contains(handle))
            {
                if (_recursionWarned.Add(handle))
                    _world?.Log($"lua: suppressed recursive hl.on(\"{name}\") callback invocation");

                continue;
            }

            _lua.RawGetInteger(LuaRegistry.Index, subscription.Reference);
            foreach (var argument in arguments)
                Push(argument);

            var before = _cause;
            Volatile.Write(ref _cause, new EventCause(this, chain.Add(handle)));
            try
            {
                Guarded(EventLimit, $"hl.on(\"{name}\") callback", arguments: arguments.Length);
            }
            finally
            {
                Volatile.Write(ref _cause, before);
            }
        }
    }

    private void Push(EventArgument argument)
    {
        switch (argument)
        {
            // No window or workspace is nil, as in the pinned release; one that is gone is an expired object.
            case WindowEventArgument { Window: null }:
            case WorkspaceEventArgument { Workspace: null }:
                _lua.PushNil();
                break;
            case WindowEventArgument window:
                Make(_makeWindow, Address(window.Window!.Value));
                break;
            case WorkspaceEventArgument workspace:
                Make(_makeWorkspace, workspace.Workspace!.Value.Name);
                break;
            case MonitorEventArgument monitor:
                Make(_makeMonitor, monitor.Name);
                break;
            case NumberEventArgument number:
                _lua.PushInteger(number.Value);
                break;
            case TextEventArgument text:
                _lua.PushString(text.Value);
                break;
            case BooleanEventArgument flag:
                _lua.PushBoolean(flag.Value);
                break;
            default:
                _lua.PushNil();
                break;
        }
    }

    // Pushes the object a constructor makes for key. A key of null makes an expired object.
    private void Make(int constructor, string? key)
    {
        _lua.RawGetInteger(LuaRegistry.Index, constructor);
        if (key is null)
            _lua.PushNil();
        else
            _lua.PushString(key);

        if (_lua.PCall(1, 1, 0) != LuaStatus.OK)
        {
            _lua.Pop(1);
            _lua.PushNil();
        }
    }

    internal static string Address(WindowId window) => "0x" + window.Value.ToString("x", CultureInfo.InvariantCulture);

    // What the queries read from the events: the focus history, the urgent windows and the workspaces visited.
    private void Track(StreamEvent streamEvent)
    {
        switch (streamEvent)
        {
            case FocusChanged { Window: WindowId focused }:
                var address = Address(focused);
                Visit(_focusHistory, address);
                _urgent.Remove(address);
                break;
            case ClientClosed closed:
                _focusHistory.Remove(Address(closed.Window));
                _urgent.Remove(Address(closed.Window));
                break;
            case ClientUrgent urgent:
                if (!_urgent.Contains(Address(urgent.Window)))
                    _urgent.Add(Address(urgent.Window));
                break;
            case WorkspaceActivated activated:
                Visit(_workspaceHistory, activated.Workspace.Name);
                break;
            case WorkspaceDestroyed destroyed:
                _workspaceHistory.Remove(destroyed.Workspace.Name);
                break;
        }
    }

    // Moves key to the end of a history, the most recent place, and forgets the oldest past the limit.
    private static void Visit(List<string> history, string key)
    {
        history.Remove(key);
        history.Add(key);
        if (history.Count > HistoryLimit)
            history.RemoveAt(0);
    }

    // history(kind): the focus history, the urgent windows or the workspaces visited, the most recent last.
    private int History(IntPtr state)
    {
        var kind = _lua.IsString(1) ? _lua.ToString(1, false) : "";
        var history = kind switch
        {
            "focus" => _focusHistory,
            "urgent" => _urgent,
            "workspace" => _workspaceHistory,
            _ => [],
        };
        _lua.CreateTable(history.Count, 0);
        for (var i = 0; i < history.Count; i++)
        {
            _lua.PushString(history[i]);
            _lua.RawSetInteger(-2, i + 1);
        }

        return 1;
    }
}
