using System.Text.Json;
using Winland;

namespace Winland.Tests.Conformance;

/// <summary>
/// The reference test harness's desktop, rebuilt on the core: two 1920x1080 monitors side by side, its config,
/// workspace 1 focused and the pointer in the middle of the first monitor. A conformance test drives it with the
/// same commands the reference case sends over its socket, and checks the same replies.
/// </summary>
internal sealed class Desk
{
    /// <summary>The reference harness's config, in the line-based syntax. Options Winland does not read are left out.</summary>
    internal const string Config = """
        general {
            gaps_in = 5
            gaps_out = 20
            border_size = 2
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

    internal static readonly Rectangle First = new(0, 0, 1920, 1080);

    internal static readonly Rectangle Second = new(1920, 0, 1920, 1080);

    private readonly Dictionary<string, WindowId> _windows = new(StringComparer.Ordinal);
    private long _next = 1;

    private readonly Dictionary<WindowId, Rectangle> _placed = [];
    private readonly Dictionary<ClientId, WindowId> _clients = [];

    internal Desk(string extra = "")
    {
        var document = ConfigParser.Parse(Config + "\n" + extra);
        Core = new Core();
        Handle(new Configure(document));
        Handle(new MonitorsChanged([Monitor("HEADLESS-1", First), Monitor("HEADLESS-2", Second)]));
        Pointer = new Point(960, 540);
        Handle(new PointerMoved(Pointer));
    }

    internal Core Core { get; }

    internal Point Pointer { get; private set; }

    /// <summary>Opens a window with this class where the reference harness opens kitty, and returns its id.</summary>
    internal WindowId Spawn(string windowClass, string title = "")
    {
        var id = new WindowId(_next++);
        _windows[windowClass] = id;
        Handle(new WindowAppeared(id, WorkArea())
        {
            Pointer = Pointer,
            Traits = WindowTraits.Normal with { Class = windowClass, Title = title.Length == 0 ? windowClass : title },
            Bounds = new Rectangle(Pointer.X - 320, Pointer.Y - 240, 640, 480),
        });
        return id;
    }

    internal WindowId Window(string windowClass) => _windows[windowClass];

    /// <summary>Closes the window as its application would, as the reference harness kills kitty.</summary>
    internal void Close(string windowClass) => Handle(new WindowClosed(_windows[windowClass]));

    internal void MovePointer(int x, int y)
    {
        Pointer = new Point(x, y);
        Handle(new PointerMoved(Pointer));
    }

    /// <summary>Feeds the core an event and keeps the frames it places.</summary>
    internal Outcome Handle(Input input)
    {
        var outcome = Core.Handle(input);
        Keep(outcome);
        return outcome;
    }

    /// <summary>Sends one pipe line, such as <c>dispatch focuswindow class:a</c> or <c>keyword general:gaps_in 0</c>.</summary>
    internal string Send(string line)
    {
        var reply = Core.Execute(line);
        Keep(reply.Effect);
        return reply.Text;
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

    internal static int WorkspaceId(JsonElement client) => client.GetProperty("workspace").GetProperty("id").GetInt32();

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

    internal WindowId ActiveId()
    {
        var address = ActiveWindow().GetProperty("address").GetString() ?? "";
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
    }

    private Rectangle WorkArea() => Pointer.X >= Second.X ? Second : First;

    private static string Pair(JsonElement pair) =>
        $"{pair[0].GetInt32()},{pair[1].GetInt32()}";

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static Monitor Monitor(string name, Rectangle bounds) =>
        new(name, name, bounds.X, bounds.Y, bounds.Width, bounds.Height, 1, true, bounds);
}
