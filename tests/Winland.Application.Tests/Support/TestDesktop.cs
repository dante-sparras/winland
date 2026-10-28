using System.Text.Json;

namespace Winland.Application.Tests;

/// <summary>
/// A desktop around a core that answers as the Windows adapter does: one 1920x1080 Monitor, a shared config,
/// Workspace 1 focused and the pointer in the middle of the Monitor. A test drives it with pipe lines and input
/// events, and reads the replies.
/// </summary>
internal sealed class TestDesktop
{
    /// <summary>The config every desktop starts with.</summary>
    internal const string Config = """
        general {
            gaps_in = 5
            gaps_out = 20
            border_size = 2
            col.active_border = rgba(33ccffee)
            col.inactive_border = rgba(595959aa)
            resize_on_border = false
            layout = dwindle
        }
        decoration {
            rounding = 10
        }
        animations {
            enabled = false
        }
        dwindle {
            preserve_split = true
        }
        master {
            new_status = master
        }
        scrolling {
            column_width = 0.5
            explicit_column_widths = 0.25, 0.333, 0.5, 0.667, 0.75, 1.0
        }
        input {
            follow_mouse = 1
        }
        windowrule = float, class:^(wr_kitty)$
        windowrule = size 200 200, class:^(wr_kitty)$
        """;

    /// <summary>Where the desktop's one Monitor, HEADLESS-2, is.</summary>
    internal static readonly Rectangle First = new(0, 0, 1920, 1080);

    /// <summary>Where a second Monitor a test plugs in goes: to the right of HEADLESS-2.</summary>
    internal static readonly Rectangle Second = new(1920, 0, 1920, 1080);

    private readonly Dictionary<string, WindowId> _windows = new(StringComparer.Ordinal);
    private long _next = 1;

    private readonly Dictionary<WindowId, Rectangle> _placed = [];
    private readonly Dictionary<ClientId, WindowId> _clients = [];
    private readonly List<Monitor> _monitors = [];
    private State _state = new([]);

    /// <summary>
    /// Builds the desktop. <paramref name="extra"/> is config after <see cref="Config"/>: the options a test sets
    /// before its first window opens. <c>reload</c> reads the same text again.
    /// </summary>
    internal TestDesktop(string extra = "")
    {
        var text = Config + "\n" + extra;
        WindowManager = new WindowManager { Cursor = new PointerReader(this) };
        WindowManager.ReadConfig = () => new ConfigRead(ConfigParser.Parse(text));
        Handle(new Configure(ConfigParser.Parse(text)));
        Pointer = new Point(960, 540);
        AddMonitor("HEADLESS-2", First);
        Handle(new PointerMoved(Pointer));
    }

    internal WindowManager WindowManager { get; }

    /// <summary>Makes <c>reload</c> read <see cref="Config"/> alone, and drop the options the test built the desktop with.</summary>
    internal void ReloadWithoutExtra() =>
        WindowManager.ReadConfig = () => new ConfigRead(ConfigParser.Parse(Config));

    /// <summary>Where the pointer is. It follows the warps the core asks for, as the system cursor does.</summary>
    internal Point Pointer { get; private set; }

    /// <summary>
    /// Opens a window with this class in the middle of the focused Monitor, and returns
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

    /// <summary>Closes the window as its application would.</summary>
    internal void Close(string windowClass) => Handle(new WindowClosed(_windows[windowClass]));

    /// <summary>Closes every window as its application would.</summary>
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

    /// <summary>Puts a floating window at a frame, as the system does when the user drags it there.</summary>
    internal void Place(string windowClass, int x, int y, int width, int height) =>
        Place(_windows[windowClass], x, y, width, height);

    internal void Place(WindowId window, int x, int y, int width, int height)
    {
        var bounds = new Rectangle(x, y, width, height);
        Handle(new WindowMoveStarted(window, Pointer));
        Handle(new WindowMoveEnded(window, Pointer, bounds));

        // The core may have kept the window on screen, so the frame is read back.
        var client = Clients().Single(candidate => IdOf(candidate) == window);
        var at = client.GetProperty("at");
        var size = client.GetProperty("size");
        _placed[window] = new Rectangle(at[0].GetInt32(), at[1].GetInt32(), size[0].GetInt32(), size[1].GetInt32());
    }

    /// <summary>Moves a floating window to this position and keeps its size.</summary>
    internal void MoveTo(WindowId window, int x, int y)
    {
        var size = Placed(window);
        Place(window, x, y, size.Width, size.Height);
    }

    /// <summary>Presses and releases a mouse button where the pointer is.</summary>
    internal void Click(string button = "mouse:272")
    {
        Handle(new PointerButtonPressed(button) { Position = Pointer });
        Handle(new PointerButtonReleased(button) { Position = Pointer });
    }

    /// <summary>Plugs in a Monitor at these bounds, with no reserved area.</summary>
    internal void AddMonitor(string name, Rectangle bounds)
    {
        _monitors.Add(Display(name, name, bounds));
        Handle(new MonitorsChanged([.. _monitors]));
    }

    /// <summary>Reports every plugged-in monitor enabled or disabled, as Windows does. Config cannot do this.</summary>
    internal void ReportEnabled(bool enabled)
    {
        for (var i = 0; i < _monitors.Count; i++)
            _monitors[i] = _monitors[i] with { Enabled = enabled };

        Handle(new MonitorsChanged([.. _monitors]));
    }

    /// <summary>Unplugs a Monitor.</summary>
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
        var outcome = WindowManager.Handle(input);
        Follow(outcome);
        return outcome;
    }

    /// <summary>
    /// Sends one pipe line, such as <c>dispatch focuswindow class:a</c> or <c>keyword general:gaps_in 0</c>.
    /// </summary>
    internal string Send(string line)
    {
        var reply = WindowManager.Execute(line);
        Follow(reply.Effect);
        return reply.Text;
    }

    private void Follow(Outcome outcome, WindowId? echoed = null)
    {
        Keep(outcome);
        if (outcome.Warp is WarpTo warp)
            Pointer = warp.Position;

        if (outcome.Focus is WindowId focus && focus != echoed)
            Follow(WindowManager.Handle(new WindowFocused(focus, FocusCause.Requested)), focus);
    }

    /// <summary>
    /// Where the window was last placed: the frame Winland moves it to, inside its border. The replies report this
    /// as <c>at</c> and <c>size</c>.
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

    /// <summary>The <c>at</c> of a client, written as <c>x,y</c>.</summary>
    internal static string At(JsonElement client) => Pair(client.GetProperty("at"));

    /// <summary>The <c>size</c> of a client, written as <c>w,h</c>.</summary>
    internal static string SizeOf(JsonElement client) => Pair(client.GetProperty("size"));

    internal static string ClassOf(JsonElement client) => client.GetProperty("class").GetString() ?? "";

    internal static int Fullscreen(JsonElement client) => client.GetProperty("fullscreen").GetInt32();

    internal static int FullscreenClient(JsonElement client) => client.GetProperty("fullscreenClient").GetInt32();

    internal static bool Floating(JsonElement client) => client.GetProperty("floating").GetBoolean();

    internal static bool Pinned(JsonElement client) => client.GetProperty("pinned").GetBoolean();

    // A named or special Workspace has no id in the reply, and reads as 0 here.
    internal static int WorkspaceId(JsonElement client) =>
        client.GetProperty("workspace").TryGetProperty("id", out var id) ? id.GetInt32() : 0;

    /// <summary>The active window's class.</summary>
    internal string ActiveClass() => ClassOf(ActiveWindow());

    /// <summary>The active window's frame, written as <c>x,y</c> and <c>w,h</c>.</summary>
    internal (string At, string Size) ActivePlace()
    {
        var bounds = Placed(ActiveId());
        return ($"{bounds.X},{bounds.Y}", $"{bounds.Width},{bounds.Height}");
    }

    /// <summary>A window's frame, written as <c>x,y</c> and <c>w,h</c>.</summary>
    internal (string At, string Size) PlaceOf(string windowClass)
    {
        var bounds = Placed(windowClass);
        return ($"{bounds.X},{bounds.Y}", $"{bounds.Width},{bounds.Height}");
    }

    /// <summary>Checks the active window's place.</summary>
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

    private sealed class PointerReader(TestDesktop desk) : ICursorPosition
    {
        public Point Read() => desk.Pointer;
    }
}
