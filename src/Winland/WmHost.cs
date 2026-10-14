using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Winland.Core;
using Winland.Core.Cli;
using Winland.Core.Diagnostics;
using Winland.Core.Shell;
using Winland.Platform.Win32;
using Winland.Core.Binds;
using Winland.Core.Chords;
using Winland.Core.Input;
using Winland.Core.Tiling;
using Winland.Ipc;
using Winland.Platform;

namespace Winland;

/// <summary>
/// The window manager's thread. It owns the hook, the tiler, the pipes, and the group strips.
/// The shell is another process: it pushes state. Nothing here waits on the shell.
/// </summary>
internal sealed class WmHost : IWinlandEngine
{
    private readonly WindowGateway _windows = new();
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly List<(Chord, bool)> _batch = [];
    private TileSettings _settings = new();
    private string _settingsPath = "";
    private KeyboardHook _hook = null!;
    private BindBook _binds = null!;
    private ChordSwitch _chordTable = null!;
    private ShellController _shell = null!;
    private WinlandServer _server = null!;
    private ChordFileWatch? _chords;
    private WindowWatcher? _watcher;
    private WindowMotion? _motion;
    private FrameSample _pace = new();
    private uint _id;
    private int _turnQueued;
    private int _draining;
    private int _lockReleased;
    private int _stopping;
    private int _exitPosted;
    private int _pressX;
    private int _pressY;
    private int _havePress;
    private DateTime _settingsAt;
    private string _picture = "";
    private string _strips = "";
    private GroupStripHost? _groupStrips;

    /// <summary>Creates the queue, builds the tiler, and pumps until the process is asked to leave.</summary>
    public void Run()
    {
        // Peek creates the thread queue, so a later PostThreadMessage has somewhere to land.
        WmPump.PeekMessage(out _, 0, 0, 0, 0);
        _id = WmPump.GetCurrentThreadId();
        Build();
        Post(Start);
        WmPump.SetTimer(0, 1, 200, 0);
        while (WmPump.GetMessage(out var message, 0, 0, 0))
        {
            if (message.Message == WmPump.Work)
            {
                DrainWork();
                continue;
            }

            if (message.Message == WmPump.Timer)
            {
                Turn();
                continue;
            }

            WmPump.TranslateMessage(ref message);
            WmPump.DispatchMessage(ref message);
        }
    }

    /// <inheritdoc />
    public void Apply(ShellPush state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var keys = new KeptKey[state.Keys.Count];
        for (var i = 0; i < state.Keys.Count; i++)
        {
            var key = state.Keys[i];
            keys[i] = new KeptKey(key.Vk, key.Super, key.Ctrl, key.Alt, key.Id);
        }

        // The hook reads this on its next key. The pipe thread does not wait for the tiler.
        ShellState.Apply(state.PickerOpen, state.Escape, state.SessionLocked, state.SurfaceHeld, keys);
        _binds.Ask(BindConditions.Escape, state.Escape);
        if (state.SessionLocked)
        {
            if (Interlocked.Exchange(ref _lockReleased, 1) == 0)
            {
                _hook.ReleaseForLock();
            }
        }
        else
        {
            Interlocked.Exchange(ref _lockReleased, 0);
        }

        Post(() => ApplyBars(state));
    }

    /// <inheritdoc />
    public string Query(string name, bool json) => Call(() => QueryHere(name, json));

    /// <inheritdoc />
    public string? Dispatch(string command, IReadOnlyList<string> args) => Call(() =>
    {
        var error = Act(command, args);
        PublishAll();
        return error;
    });

    private void Build()
    {
        _settingsPath = Path.Combine(ConfigFolder.Path, TileSettingsFile.FileName);
        _settings = TileSettingsFile.LoadOrDefaults(_settingsPath);
        _settingsAt = File.Exists(_settingsPath) ? File.GetLastWriteTimeUtc(_settingsPath) : DateTime.MinValue;
        ElevatedBroker.Allowed = _settings.AdministratorWindows;
        var bindings = Path.Combine(ConfigFolder.Path, ChordBook.FileName);
        var book = ChordBook.Read(bindings);
        foreach (var warning in book.Warnings)
        {
            Log.Warning("Chord", warning);
        }

        _chordTable = new ChordSwitch(book);
        _binds = new BindBook(new ShellBinds(_chordTable));
        _binds.Add(EscapeBinds.Create());
        _hook = new KeyboardHook(() =>
        {
            _windows.WakePlacer();
            _server?.Publish(Wire.Scene("activity", ""));
        });
        _hook.UseChords(new ShellBinds(new PageActionGate(_chordTable, new FrontProgram())));
        _hook.UseBinds(_binds);
        _pace = new FrameSample();
        _shell = new ShellController(
            _windows,
            new IgnoredActions(),
            _settings,
            NoHooks.Instance,
            _pace,
            superHeld: () => KeyboardHook.SuperIsHeld,
            chords: _chordTable,
            surfaceHeld: () => ShellState.SurfaceHeld);
        var sink = new PublishingSink(line => _server?.Publish(line));
        _shell.UseGlobalSink(sink);
        _hook.UseSink(sink);
        _server = new WinlandServer(this, BuildStamp.Value, message => Log.Warning("Winland", message));
        _hook.ReportPicker = key => _server.Publish(Wire.Picker(key));
        _hook.ChordQueued += () => Post(() =>
        {
            Drain(flush: false);
            _hook.DeliverHeld();
            PublishAll();
        });
        _windows.Dragged += drag => Post(() => _shell.ApplyDrag(drag));
        _windows.WorkspaceWheeled += cycle => Post(() => _shell.Run(new WinlandCommand.CycleWorkspace(cycle)));
        _windows.PointerPressed += NotePress;
        _chords = new ChordFileWatch(
            bindings,
            () => null,
            next => Post(() =>
            {
                foreach (var warning in next.Warnings)
                {
                    Log.Warning("Chord", warning);
                }

                _chordTable.Use(next);
            }));
    }

    private void Start()
    {
        _server.Start();
        _hook.FullscreenKey = _windows.TakeFullscreenKey;
        ElevatedBroker.Keyed += message =>
        {
            if (message is HookWire.ChordKeyed keyed)
            {
                _hook.Forward(keyed.Chord, keyed.Down);
            }
        };
        _hook.Install();
        WindowFade.RestoreLeft();
        _watcher = new WindowWatcher(() => AskTurn(), _windows.Departed, _windows.MayCount, ownThread: true, shown: _windows.FadeShown, made: hwnd =>
        {
            if (!WindowGateway.MayBecomeTile(hwnd))
            {
                return;
            }

            _windows.FadeMade(hwnd);
            Post(() =>
            {
                if (_shell.Workspaces.SeveralInFront)
                {
                    _shell.Seat(hwnd);
                }
            });
        });
        _motion = new WindowMotion(
            AskTurn,
            hwnd =>
            {
                if (!ShellProperty.IsTagged(hwnd))
                {
                    Post(() => _windows.HoldTile(hwnd));
                }
            },
            (parent, child) =>
            {
                if (!ShellProperty.IsTagged(parent))
                {
                    Post(() => _windows.ChildMoved(parent, child));
                }
            },
            renamed: null,
            pointerMoved: null,
            foreground: hwnd =>
            {
                if (hwnd == 0 || ShellProperty.IsTagged(hwnd))
                {
                    return;
                }

                ElevatedBroker.InFront(hwnd);
                Post(FollowKeyboard);
            });
        _pace.Start(Environment.TickCount64);
        _groupStrips = new GroupStripHost(id => _shell.ShowGroupMember(id));
        KeepGroupBar();
        try
        {
            _shell.Arrange();
        }
        catch (Exception ex)
        {
            Log.Warning("Winland", "the first arrange failed", ex);
        }

        PublishAll();
        Log.Information("Winland", "ready");
        _shell.Placed += PublishAll;
    }

    private void Turn()
    {
        DrainWork();
        if (Volatile.Read(ref _stopping) != 0)
        {
            return;
        }

        PollSettings();
        Drain(flush: false);
        _hook.WatchStall();
        _shell.SettleKeyboard();
        _hook.DeliverHeld();
        FollowPointer();
        var groupBar = _shell.CurrentSession.GroupBar;
        KeepGroupBar();
        if (_shell.MonitorsChanged || !_windows.TilesHold() || _shell.CurrentSession.GroupBar != groupBar)
        {
            try
            {
                _shell.Arrange();
            }
            catch (Exception ex)
            {
                Log.Warning("Winland", "arrange failed", ex);
            }
        }

        PublishAll();
    }

    private void ApplyBars(ShellPush state)
    {
        if (Volatile.Read(ref _stopping) != 0)
        {
            return;
        }

        var edge = EdgeOf(state.BarEdge);
        _windows.SetBar(state.BarThickness, edge);
        var bars = new nint[state.Bars?.Count ?? 0];
        if (state.Bars is not null)
        {
            for (var i = 0; i < state.Bars.Count; i++)
            {
                bars[i] = (nint)state.Bars[i];
            }
        }

        _windows.ReserveBars(bars, edge);
        KeepGroupBar();
        try
        {
            _shell.Arrange();
        }
        catch (Exception ex)
        {
            Log.Warning("Winland", "arrange failed", ex);
        }

        PublishAll();
    }

    private string QueryHere(string name, bool json)
    {
        if (name.Equals("placed", StringComparison.Ordinal))
        {
            return WmPicture.WriteStrips(Strips());
        }

        if (name is not ("desktop" or "workspace" or "workspaces" or "clients" or "activewindow"))
        {
            return "";
        }

        var shot = _shell.Facts();
        var view = Picture(shot);
        if (name.Equals("desktop", StringComparison.Ordinal))
        {
            return json ? DesktopDocument.Write(shot) : "workspace " + view.Current.ToString(CultureInfo.InvariantCulture);
        }

        if (name.Equals("workspace", StringComparison.Ordinal))
        {
            return WmPicture.Write(view);
        }

        if (name.Equals("workspaces", StringComparison.Ordinal))
        {
            return json ? WorkspacesJson(shot) : WorkspacesText(shot);
        }

        if (name.Equals("clients", StringComparison.Ordinal))
        {
            return json ? ClientsJson(shot) : ClientsText(shot);
        }

        return json ? ActiveJson(shot) : ActiveText(shot);
    }

    private string? Act(string command, IReadOnlyList<string> args)
    {
        if (Volatile.Read(ref _stopping) != 0)
        {
            return null;
        }

        switch (command)
        {
            case "stop":
                GiveUp(giveBack: true);
                return null;
            case "replace":
                GiveUp(giveBack: false);
                return null;
            case "close-all":
                _shell.CloseAll();
                return null;
            case "show-hidden":
                _shell.ShowHidden();
                return null;
            case "note-launch":
                _shell.NoteLaunch();
                return null;
            case "flush":
                _shell.FlushLayout();
                return null;
            case "group-show":
                if (args.Count == 0 || !long.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    return "group-show needs a window.";
                }

                _shell.ShowGroupMember(id);
                return null;
            case "focus-app":
                var name = args.Count == 0 ? "" : string.Join(' ', args);
                if (name.Length == 0)
                {
                    return "focus-app needs a name.";
                }

                return _shell.FocusApp(name) ? null : name + " is not open.";
        }

        if (!ChordCommands.TryParse(command, out var parsed) || parsed is null)
        {
            return "unknown command " + command;
        }

        if (WinlandShell.ToWinland(parsed) is null)
        {
            return "the shell runs that";
        }

        var spacing = _settings.SpacingEnabled;
        var square = _settings.SquareTileEnabled;
        _shell.Run(parsed);
        if (spacing != _settings.SpacingEnabled || square != _settings.SquareTileEnabled)
        {
            SaveToggles();
        }

        return null;
    }

    private void GiveUp(bool giveBack)
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
        {
            return;
        }

        if (giveBack)
        {
            if (!Lifetime.TryWrite(Path.Combine(ConfigFolder.Path, Lifetime.FileName), LifetimeWord.Stopped, out var life))
            {
                Log.Warning("Winland", "could not write lifetime", life);
            }

            try
            {
                _shell.ShowHidden();
            }
            catch (Exception ex)
            {
                Log.Warning("Winland", "showing hidden windows failed", ex);
            }

            try
            {
                _windows.RestoreCaptions();
            }
            catch (Exception ex)
            {
                Log.Warning("Winland", "giving the caption buttons back failed", ex);
            }
        }

        try
        {
            _groupStrips?.Close();
        }
        catch (Exception ex)
        {
            Log.Warning("Winland", "the group strips did not close", ex);
        }

        try
        {
            _hook.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warning("Winland", "the hook did not stop", ex);
        }

        if (Interlocked.Exchange(ref _exitPosted, 1) == 0)
        {
            // The reply is written after this returns. The mutex stays held until the process leaves.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(150);
                Environment.Exit(0);
            });
        }
    }

    private void KeepGroupBar()
    {
        var pixels = GroupStripHost.StripPixels();
        if (_shell.CurrentSession.GroupBar == pixels)
        {
            return;
        }

        _shell.SetGroupBar(pixels);
    }

    private void DrawStrips()
    {
        if (_groupStrips is null || Volatile.Read(ref _stopping) != 0)
        {
            return;
        }

        try
        {
            _groupStrips.Sync(_shell.GroupStrips(), _windows.Caption);
        }
        catch (Exception ex)
        {
            Log.Warning("Winland", "group strips failed", ex);
        }
    }

    private void PublishAll()
    {
        DrawStrips();
        if (_server is null || Volatile.Read(ref _stopping) != 0)
        {
            return;
        }

        try
        {
            var picture = WmPicture.Write(Picture(_shell.Facts()));
            if (!string.Equals(picture, _picture, StringComparison.Ordinal))
            {
                _picture = picture;
                _server.Publish(Wire.Scene("workspace", picture));
            }

            var strips = WmPicture.WriteStrips(Strips());
            if (!string.Equals(strips, _strips, StringComparison.Ordinal))
            {
                _strips = strips;
                _server.Publish(Wire.Scene("placed", strips));
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Winland", "the scene was not published", ex);
        }
    }

    private WmPicture.View Picture(TilerFacts shot)
    {
        var occupancy = new int[10];
        var front = new int[10];
        Array.Fill(front, -1);
        // Slot 0 is unused. Occupancy refuses it: a Workspace number runs from 1 through 9.
        for (var number = 1; number <= Winland.Core.Workspaces.WorkspaceBook.Maximum; number++)
        {
            occupancy[number] = _shell.Workspaces.Occupancy(number);
            front[number] = _shell.FrontMonitor(number);
        }

        var screens = _windows.Screens;
        var picture = new WmPicture.Screen[screens.Count];
        for (var i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            picture[i] = new(screen.X, screen.Y, screen.Width, screen.Height, _shell.FullscreenOn(screen));
        }

        var home = _shell.FrontScreen();
        var clients = new WmPicture.Client[shot.Windows.Count];
        for (var i = 0; i < shot.Windows.Count; i++)
        {
            var window = shot.Windows[i];
            clients[i] = new(window.Hwnd, window.Title, window.Place, window.Workspace ?? 0);
        }

        return new WmPicture.View(
            _shell.Scene,
            _shell.Workspaces.Current,
            _shell.FocusedMonitor(),
            _shell.Workspaces.SeveralInFront,
            _shell.Switching,
            _shell.ScratchpadOpen,
            _shell.CurrentSession.Windows.Count,
            occupancy,
            front,
            picture,
            home.X,
            home.Y,
            home.Width,
            home.Height,
            clients);
    }

    private WmPicture.Strip[] Strips()
    {
        var strips = _shell.GroupStrips();
        var copy = new WmPicture.Strip[strips.Count];
        for (var i = 0; i < strips.Count; i++)
        {
            var strip = strips[i];
            copy[i] = new(strip.Rect.X, strip.Rect.Y, strip.Rect.Width, strip.Rect.Height, [.. strip.Members], strip.Active);
        }

        return copy;
    }

    private void Drain(bool flush)
    {
        if (_draining != 0 || Volatile.Read(ref _stopping) != 0)
        {
            return;
        }

        _draining = 1;
        try
        {
            while (true)
            {
                var batch = _batch;
                batch.Clear();
                var oldest = long.MaxValue;
                while (_hook.TryTake(out var chord, out var down, out var queuedAt))
                {
                    batch.Add((chord, down));
                    oldest = Math.Min(oldest, queuedAt);
                }

                if (batch.Count == 0)
                {
                    if (!flush)
                    {
                        break;
                    }

                    _shell.FlushLayout();
                    if (!_hook.TryTake(out var chord, out var down, out var queuedAt))
                    {
                        break;
                    }

                    batch.Add((chord, down));
                    oldest = queuedAt;
                    while (_hook.TryTake(out chord, out down, out queuedAt))
                    {
                        batch.Add((chord, down));
                        oldest = Math.Min(oldest, queuedAt);
                    }
                }

                foreach (var (chord, down) in ChordBatch.Collapse(batch, _shell.Chords))
                {
                    try
                    {
                        _shell.HandleKey(chord, down);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Chord", "chord " + chord + " down=" + down + " failed", ex);
                    }
                }

                if (_pace.NoteChord(Environment.TickCount64, Stopwatch.GetElapsedTime(oldest).TotalMilliseconds) is string summary)
                {
                    Log.Information("Winland", summary);
                }
            }
        }
        finally
        {
            _draining = 0;
            if (flush)
            {
                _hook.DeliverHeld();
            }
        }
    }

    private void FollowPointer()
    {
        _hook.SamplePointer();
        if (!_hook.TakeHover())
        {
            return;
        }

        var buttonHeld = PointerProbe.IsDown(PointerProbe.LeftButton) || PointerProbe.IsDown(PointerProbe.RightButton);
        var crossed = _shell.Hover(_windows.WindowUnderPointer(), _windows.DrawnAtPointer(), buttonHeld, ShellState.SurfaceHeld);
        if (!crossed
            && _shell.Workspaces.SeveralInFront
            && PointerProbe.TryCursor(out var x, out var y)
            && _windows.PressedAt(x, y).Bare)
        {
            _shell.HoverDesktop(x, y, buttonHeld, ShellState.SurfaceHeld);
        }
    }

    private void NotePress(int x, int y)
    {
        if (!_shell.Workspaces.SeveralInFront)
        {
            return;
        }

        Volatile.Write(ref _pressX, x);
        Volatile.Write(ref _pressY, y);
        Volatile.Write(ref _havePress, 1);
        Post(FollowPress);
    }

    private void FollowPress()
    {
        if (Volatile.Read(ref _havePress) == 0 || ShellState.SurfaceHeld)
        {
            return;
        }

        var x = Volatile.Read(ref _pressX);
        var y = Volatile.Read(ref _pressY);
        var (tile, bare) = _windows.PressedAt(x, y);
        _shell.Press(x, y, tile, bare);
    }

    private void FollowKeyboard()
    {
        if (_shell.Workspaces.SeveralInFront)
        {
            _shell.FollowShown();
        }
    }

    private void AskTurn()
    {
        if (Interlocked.Exchange(ref _turnQueued, 1) == 1)
        {
            return;
        }

        Post(() =>
        {
            Interlocked.Exchange(ref _turnQueued, 0);
            if (Volatile.Read(ref _stopping) != 0)
            {
                return;
            }

            if (_shell.MonitorsChanged || !_windows.TilesHold())
            {
                try
                {
                    _shell.Arrange();
                }
                catch (Exception ex)
                {
                    Log.Warning("Winland", "arrange failed", ex);
                }
            }

            PublishAll();
        });
    }

    private void PollSettings()
    {
        DateTime written;
        try
        {
            written = File.Exists(_settingsPath) ? File.GetLastWriteTimeUtc(_settingsPath) : DateTime.MinValue;
        }
        catch (IOException)
        {
            return;
        }

        if (written == _settingsAt)
        {
            return;
        }

        try
        {
            _settings.CopyFrom(TileSettingsFile.LoadOrDefaults(_settingsPath));
            ElevatedBroker.Allowed = _settings.AdministratorWindows;
            _settingsAt = written;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warning("Winland", "settings.json was not read", ex);
        }
    }

    private void SaveToggles()
    {
        try
        {
            TileSettingsFile.WriteToggles(_settingsPath, _settings.SpacingEnabled, _settings.SquareTileEnabled);
            _settingsAt = File.GetLastWriteTimeUtc(_settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning("Winland", "the tile toggles were not saved", ex);
        }
    }

    private void Post(Action work)
    {
        _work.Enqueue(work);
        if (_id != 0)
        {
            WmPump.PostThreadMessage(_id, WmPump.Work, 0, 0);
        }
    }

    private void DrainWork()
    {
        while (_work.TryDequeue(out var work))
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                Log.Error("Winland", "posted work failed", ex);
            }
        }
    }

    private T Call<T>(Func<T> work)
    {
        if (WmPump.GetCurrentThreadId() == _id)
        {
            return work();
        }

        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        return done.Task.GetAwaiter().GetResult();
    }

    private static ScreenEdge EdgeOf(string? edge) => edge?.ToLowerInvariant() switch
    {
        "bottom" => ScreenEdge.Bottom,
        "left" => ScreenEdge.Left,
        "right" => ScreenEdge.Right,
        _ => ScreenEdge.Top,
    };

    private static string WorkspacesText(TilerFacts shot)
    {
        var text = new StringBuilder();
        foreach (var workspace in shot.Workspaces)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append("workspace ").Append(workspace.Number.ToString(CultureInfo.InvariantCulture));
            if (workspace.Front)
            {
                text.Append(" (front)");
            }

            text.Append(" windows ").Append(workspace.Occupancy.ToString(CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    private static string WorkspacesJson(TilerFacts shot)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var workspace in shot.Workspaces)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", workspace.Number);
                writer.WriteNumber("windows", workspace.Occupancy);
                writer.WriteBoolean("active", workspace.Front);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ClientsText(TilerFacts shot)
    {
        var text = new StringBuilder();
        foreach (var window in shot.Windows)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(window.Hwnd.ToString(CultureInfo.InvariantCulture));
            text.Append('\t').Append(window.Place);
            text.Append('\t').Append((window.Workspace ?? 0).ToString(CultureInfo.InvariantCulture));
            text.Append('\t').Append(window.Title);
        }

        return text.ToString();
    }

    private static string ClientsJson(TilerFacts shot)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var window in shot.Windows)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", window.Hwnd);
                writer.WriteString("title", window.Title);
                writer.WriteString("place", window.Place);
                writer.WriteNumber("workspace", window.Workspace ?? 0);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ActiveText(TilerFacts shot)
    {
        if (shot.Focused is not long id)
        {
            return "";
        }

        foreach (var window in shot.Windows)
        {
            if (window.Hwnd == id)
            {
                return window.Title;
            }
        }

        return id.ToString(CultureInfo.InvariantCulture);
    }

    private static string ActiveJson(TilerFacts shot)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (shot.Focused is long id)
            {
                writer.WriteNumber("id", id);
                var title = "";
                var workspace = 0;
                foreach (var window in shot.Windows)
                {
                    if (window.Hwnd == id)
                    {
                        title = window.Title;
                        workspace = window.Workspace ?? 0;
                        break;
                    }
                }

                writer.WriteString("title", title);
                writer.WriteNumber("workspace", workspace);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private sealed class PublishingSink(Action<string> publish) : IGlobalBindSink
    {
        public void OnGlobal(string name, bool down, bool hold) => publish(Wire.Bind(name, down, hold));
    }
}
