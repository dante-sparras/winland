using System.Text.Json;
using Winland;
using Winland.Lua;

namespace Winland.Tests.Conformance;

/// <summary>
/// The reference test harness's desktop, rebuilt on the core: two 1920x1080 monitors side by side, its config,
/// workspace 1 focused and the pointer in the middle of the first monitor. A conformance test drives it with the
/// same commands the reference case sends over its socket, and checks the same replies. <see cref="WithLua"/> builds
/// it with a Lua config running, as the reference's is.
/// </summary>
internal sealed class Desk : IDisposable
{
    /// <summary>The reference harness's config, in the line-based syntax. Options Winland does not read are left out.</summary>
    internal const string Config = """
        general {
            gaps_in = 5
            gaps_out = 20
            border_size = 2
            col.active_border = rgba(33ccffee) rgba(00ff99ee) 45deg
            col.inactive_border = rgba(595959aa)
            resize_on_border = false
            layout = dwindle
            snap {
                enabled = true
                window_gap = 8
                monitor_gap = 10
                respect_gaps = false
                border_overlap = false
            }
        }
        decoration {
            rounding = 10
            rounding_power = 2
        }
        animations {
            enabled = false
        }
        dwindle {
            preserve_split = true
            split_bias = 1
        }
        master {
            new_status = master
        }
        scrolling {
            fullscreen_on_one_column = true
            column_width = 0.5
            focus_fit_method = 1
            follow_focus = true
            follow_min_visible = 1
            explicit_column_widths = 0.25, 0.333, 0.5, 0.667, 0.75, 1.0
            wrap_focus = true
            wrap_swapcol = true
        }
        input {
            follow_mouse = 1
            sensitivity = 0
        }
        windowrulev2 = float, class:^(wr_kitty)$
        windowrulev2 = size 200 200, class:^(wr_kitty)$
        """;

    /// <summary>Where the reference harness's one monitor, HEADLESS-2, is.</summary>
    internal static readonly Rectangle First = new(0, 0, 1920, 1080);

    /// <summary>Where a monitor a case creates goes: to the right of HEADLESS-2, as the reference's auto-right rule puts it.</summary>
    internal static readonly Rectangle Second = new(1920, 0, 1920, 1080);

    private readonly Dictionary<string, WindowId> _windows = new(StringComparer.Ordinal);
    private long _next = 1;

    private readonly Dictionary<WindowId, Rectangle> _placed = [];
    private readonly Dictionary<ClientId, WindowId> _clients = [];
    private readonly List<Monitor> _monitors = [];
    private State _state = new([]);

    // The Lua's dispatches reach the desk from the Lua thread, so the desk's own state is changed under this.
    private readonly Lock _gate = new();
    private readonly LuaRuntime? _lua;
    private LuaSession? _pending;

    /// <summary>
    /// Builds the desktop. <paramref name="extra"/> is config after the reference config: the options a case sets
    /// before its first window opens. <c>reload</c> reads the same text again.
    /// </summary>
    internal Desk(string extra = "")
        : this(extra, lua: null, files: null)
    {
    }

    private Desk(string extra, string? lua, IReadOnlyDictionary<string, string>? files)
    {
        var text = Config + "\n" + extra;
        Core = new Core { Cursor = new PointerReader(this) };
        if (lua is null)
        {
            Core.ReadConfig = () => new ConfigRead(ConfigParser.Parse(text));
            Handle(new Configure(ConfigParser.Parse(text)));
        }
        else
        {
            // A bind to a Lua function names Winland's own dispatcher, which the daemon binds. No case presses one.
            _ = Dispatchers.TryBind(LuaConfig.FunctionDispatcher, _ => new DispatcherResult(null, null), out _);
            var runtime = new LuaRuntime(new LuaWorld(this));
            _lua = runtime;
            runtime.Listen(Core);
            Core.ReadConfig = () => new ConfigRead(ReadLua(text, lua, files));
            Core.AfterReload = _ => runtime.Adopt(TakeLua());
            Handle(new Configure(ReadLua(text, lua, files)));
        }

        Pointer = new Point(960, 540);
        AddMonitor("HEADLESS-2", First);
        Handle(new PointerMoved(Pointer));
        _lua?.Adopt(TakeLua());
    }

    /// <summary>The folder of the Lua config of <see cref="WithLua"/>. Nothing is written there.</summary>
    internal static readonly string LuaFolder = Path.Combine(Path.GetTempPath(), "winland-desk");

    /// <summary>
    /// Builds the desktop with <paramref name="lua"/> as a running Lua config, read from <c>winland.lua</c> in
    /// <see cref="LuaFolder"/> after the reference config. <paramref name="files"/> are the files it can require, by
    /// their path from that folder with forward slashes. <c>eval</c>, <c>repl</c> and a <c>dispatch</c> with a Lua
    /// call in it go to the Lua, as the daemon routes them, and the Lua's dispatches come back as pipe commands do.
    /// <c>reload</c> runs the Lua again.
    /// </summary>
    internal static Desk WithLua(string lua, string extra = "", IReadOnlyDictionary<string, string>? files = null) =>
        new(extra, lua, files);

    /// <summary>What the running Lua wrote to the log.</summary>
    internal List<string> LuaLog { get; } = [];

    // A read of the Lua config: the reference config, then the lines its hl calls make. Its Lua waits until the
    // config it came with is in use, as the daemon's does.
    private ConfigDocument ReadLua(string text, string lua, IReadOnlyDictionary<string, string>? files)
    {
        var load = LuaConfig.Load(lua, Path.Combine(LuaFolder, "winland.lua"), full =>
        {
            var relative = Path.GetRelativePath(LuaFolder, full).Replace(Path.DirectorySeparatorChar, '/');
            return files is not null && files.TryGetValue(relative, out var content) ? content : null;
        });
        _pending?.Dispose();
        _pending = load.Session;
        return ConfigParser.Parse(text + "\n" + load.Translation.Text);
    }

    private LuaSession? TakeLua()
    {
        var session = _pending;
        _pending = null;
        return session;
    }

    public void Dispose()
    {
        _lua?.Dispose();
        _pending?.Dispose();
    }

    internal Core Core { get; }

    /// <summary>Where the pointer is. It follows the warps the core asks for, as the system cursor does.</summary>
    internal Point Pointer { get; private set; }

    /// <summary>
    /// Opens a window with this class on the focused monitor, where the reference harness opens kitty, and returns
    /// its id. Each window has a process of its own. A second window with the same class takes over the name.
    /// </summary>
    internal WindowId Spawn(string windowClass, string title = "", WindowTraits? traits = null, bool shownMaximized = false, WindowId? owner = null)
    {
        var id = new WindowId(_next++);
        _windows[windowClass] = id;
        var area = FocusedArea();
        Handle(new WindowAppeared(id, area)
        {
            Pointer = Pointer,
            Traits = (traits ?? WindowTraits.Normal) with { Class = windowClass, Title = title.Length == 0 ? windowClass : title },
            Bounds = new Rectangle(area.X + (area.Width / 2) - 320, area.Y + (area.Height / 2) - 240, 640, 480),
            ProcessId = 1000 + id.Value,
            ShownMaximized = shownMaximized,
            Owner = owner,
        });
        return id;
    }

    internal WindowId Window(string windowClass) => _windows[windowClass];

    /// <summary>Closes the window as its application would, as the reference harness kills kitty.</summary>
    internal void Close(string windowClass) => Handle(new WindowClosed(_windows[windowClass]));

    /// <summary>Closes every window as its application would, as the reference harness kills all its windows.</summary>
    internal void CloseAll()
    {
        foreach (var client in Clients())
            Handle(new WindowClosed(IdOf(client)));
    }

    /// <summary>Closes the active window as its application would.</summary>
    internal void CloseActive() => Handle(new WindowClosed(ActiveId()));

    internal void MovePointer(int x, int y)
    {
        Pointer = new Point(x, y);
        Handle(new PointerMoved(Pointer));
    }

    /// <summary>Presses and releases a mouse button where the pointer is.</summary>
    internal void Click(string button = "mouse:272")
    {
        Handle(new PointerButtonPressed(button) { Position = Pointer });
        Handle(new PointerButtonReleased(button) { Position = Pointer });
    }

    /// <summary>Plugs in a monitor, as the reference's <c>output create headless NAME</c> does.</summary>
    internal void AddMonitor(string name, Rectangle bounds)
    {
        _monitors.Add(Monitor(name, bounds));
        Handle(new MonitorsChanged([.. _monitors]));
    }

    /// <summary>Unplugs a monitor, as the reference's <c>output remove NAME</c> does.</summary>
    internal void RemoveMonitor(string name)
    {
        _monitors.RemoveAll(monitor => monitor.Name == name);
        Handle(new MonitorsChanged([.. _monitors]));
    }

    /// <summary>
    /// Feeds the core an event, keeps the frames it places, and answers as the Windows adapter does: the window the
    /// outcome focuses reports focus back, and the pointer goes where the outcome warps it.
    /// </summary>
    internal Outcome Handle(Input input)
    {
        lock (_gate)
        {
            var outcome = Core.Handle(input);
            Follow(outcome);
            return outcome;
        }
    }

    /// <summary>
    /// Sends one pipe line, such as <c>dispatch focuswindow class:a</c> or <c>keyword general:gaps_in 0</c>. With a
    /// Lua config, the lines the Lua answers go to it.
    /// </summary>
    internal string Send(string line) => _lua?.Answer(line, Execute) ?? Execute(line);

    private string Execute(string line)
    {
        lock (_gate)
        {
            var reply = Core.Execute(line);
            Follow(reply.Effect);
            return reply.Text;
        }
    }

    private void Follow(Outcome outcome, WindowId? echoed = null)
    {
        Keep(outcome);
        if (outcome.Warp is WarpTo warp)
            Pointer = warp.Position;

        if (outcome.Focus is WindowId focus && focus != echoed)
            Follow(Core.Handle(new WindowFocused(focus, FocusCause.Requested)), focus);
    }

    /// <summary>
    /// Where the window was last placed: the frame Winland moves it to, inside its border. The reference
    /// replies report this as <c>at</c> and <c>size</c>.
    /// </summary>
    internal Rectangle Placed(string windowClass) => Placed(_windows[windowClass]);

    internal Rectangle Placed(WindowId window) =>
        _placed.TryGetValue(window, out var bounds) ? bounds : throw new InvalidOperationException($"Window {window.Value} was never placed.");

    internal string Dispatch(string dispatcher, string arguments = "") =>
        Send(arguments.Length == 0 ? $"dispatch {dispatcher}" : $"dispatch {dispatcher} {arguments}");

    internal void Ok(string dispatcher, string arguments = "") =>
        Assert.Equal("ok", Dispatch(dispatcher, arguments));

    internal void Keyword(string keyword, string value) =>
        Assert.Equal("ok", Send($"keyword {keyword} {value}"));

    internal JsonElement ActiveWindow() => Json(Send("j/activewindow"));

    internal JsonElement[] Clients() => [.. Json(Send("j/clients")).EnumerateArray()];

    internal JsonElement Client(string windowClass) =>
        Clients().Single(client => client.GetProperty("class").GetString() == windowClass);

    internal JsonElement ActiveWorkspace() => Json(Send("j/activeworkspace"));

    internal JsonElement[] Workspaces() => [.. Json(Send("j/workspaces")).EnumerateArray()];

    /// <summary>The <c>at</c> of a client, written as the reference text reply writes it: <c>x,y</c>.</summary>
    internal static string At(JsonElement client) => Pair(client.GetProperty("at"));

    /// <summary>The <c>size</c> of a client, written as the reference text reply writes it: <c>w,h</c>.</summary>
    internal static string SizeOf(JsonElement client) => Pair(client.GetProperty("size"));

    internal static string ClassOf(JsonElement client) => client.GetProperty("class").GetString() ?? "";

    internal static int Fullscreen(JsonElement client) => client.GetProperty("fullscreen").GetInt32();

    internal static int FullscreenClient(JsonElement client) => client.GetProperty("fullscreenClient").GetInt32();

    internal static bool Floating(JsonElement client) => client.GetProperty("floating").GetBoolean();

    internal static bool Pinned(JsonElement client) => client.GetProperty("pinned").GetBoolean();

    // A named or special Workspace has no id in the reply, as in the pinned release, and reads as 0 here.
    internal static int WorkspaceId(JsonElement client) =>
        client.GetProperty("workspace").TryGetProperty("id", out var id) ? id.GetInt32() : 0;

    /// <summary>The active window's class, as the reference checks <c>class: name</c>.</summary>
    internal string ActiveClass() => ClassOf(ActiveWindow());

    /// <summary>The active window's frame, written as the reference writes <c>at: x,y</c> and <c>size: w,h</c>.</summary>
    internal (string At, string Size) ActivePlace()
    {
        var bounds = Placed(ActiveId());
        return ($"{bounds.X},{bounds.Y}", $"{bounds.Width},{bounds.Height}");
    }

    /// <summary>A window's frame, written as the reference writes <c>at: x,y</c> and <c>size: w,h</c>.</summary>
    internal (string At, string Size) PlaceOf(string windowClass)
    {
        var bounds = Placed(windowClass);
        return ($"{bounds.X},{bounds.Y}", $"{bounds.Width},{bounds.Height}");
    }

    /// <summary>Checks the active window's place, as the reference checks <c>at: x,y</c> and <c>size: w,h</c>.</summary>
    internal void ExpectActive(string at, string size) => Assert.Equal((at, size), ActivePlace());

    internal WindowId ActiveId() => IdOf(ActiveWindow());

    internal static WindowId IdOf(JsonElement client)
    {
        var address = client.GetProperty("address").GetString() ?? "";
        return new WindowId(Convert.ToInt64(address[2..], 16));
    }

    private void Keep(Outcome outcome)
    {
        foreach (var client in outcome.State.Clients)
            _clients[client.Id] = client.Window;
        foreach (var client in outcome.State.Hidden)
            _clients[client.Id] = client.Window;
        foreach (var placement in outcome.Placements)
        {
            if (_clients.TryGetValue(placement.Client, out var window))
                _placed[window] = placement.Bounds;
        }

        _state = outcome.State;
    }

    /// <summary>The work area of the focused monitor, where a new window opens.</summary>
    internal Rectangle FocusedArea()
    {
        foreach (var screen in _state.Screens)
        {
            if (screen.Focused)
                return screen.Monitor.WorkArea;
        }

        return First;
    }

    private static string Pair(JsonElement pair) =>
        $"{pair[0].GetInt32()},{pair[1].GetInt32()}";

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>A headless monitor at these bounds, with no reserved area.</summary>
    internal static Monitor Monitor(string name, Rectangle bounds) =>
        new(name, name, bounds.X, bounds.Y, bounds.Width, bounds.Height, 1, true, bounds);

    private sealed class PointerReader(Desk desk) : ICursorPosition
    {
        public Point Read() => desk.Pointer;
    }

    // What the running Lua reaches: the desk's pipe, as the daemon's window host is, and its log.
    private sealed class LuaWorld(Desk desk) : ILuaWorld
    {
        public string Command(string command) => desk.Execute(command);

        public void Log(string message)
        {
            lock (desk.LuaLog)
                desk.LuaLog.Add(message);
        }
    }
}
