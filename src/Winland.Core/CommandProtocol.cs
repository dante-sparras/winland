using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winland;

public static class CommandProtocol
{
    public const string PipeName = "winland";

    public static CommandParse Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return new CommandRejected("error: unknown request");

        var text = line.Trim();
        var flags = CommandFlagReader.Strip(ref text);
        if (text.Length == 0)
            return new CommandRejected("error: unknown request");

        if (ExtraCommands.TryParse(text, flags, out var extra))
            return extra;

        var verbEnd = IndexOfWhitespace(text);
        var verb = verbEnd < 0 ? text : text[..verbEnd];
        var rest = verbEnd < 0 ? "" : text[(verbEnd + 1)..].TrimStart();

        if (verb.Equals("dispatch", StringComparison.OrdinalIgnoreCase))
        {
            if (rest.Length == 0)
                return new CommandRejected("error: missing dispatcher");

            var nameEnd = IndexOfWhitespace(rest);
            var name = nameEnd < 0 ? rest : rest[..nameEnd];
            var arguments = nameEnd < 0 ? "" : rest[(nameEnd + 1)..].TrimStart();
            return Attach(new DispatchRequest(name, arguments), flags);
        }

        if (verb.Equals("monitors", StringComparison.OrdinalIgnoreCase))
        {
            if (rest.Length == 0)
                return Attach(new MonitorsRequest(), flags);

            if (rest.Equals("all", StringComparison.OrdinalIgnoreCase))
                return Attach(new MonitorsRequest(All: true), flags);

            return new CommandRejected("error: unknown request");
        }

        if (verb.Equals("plugin", StringComparison.OrdinalIgnoreCase))
            return ParsePlugin(rest);

        if (rest.Length > 0)
            return new CommandRejected("error: unknown request");

        if (verb.Equals("clients", StringComparison.OrdinalIgnoreCase))
            return Attach(new ClientsRequest(flags.HasFlag(CommandFlags.All)), flags);

        if (verb.Equals("workspaces", StringComparison.OrdinalIgnoreCase))
            return Attach(new WorkspacesRequest(), flags);

        if (verb.Equals("workspacerules", StringComparison.OrdinalIgnoreCase))
            return Attach(new WorkspaceRulesRequest(), flags);

        if (verb.Equals("activeworkspace", StringComparison.OrdinalIgnoreCase))
            return Attach(new ActiveWorkspaceRequest(), flags);

        if (verb.Equals("activewindow", StringComparison.OrdinalIgnoreCase))
            return Attach(new ActiveWindowRequest(), flags);

        if (verb.Equals("events", StringComparison.OrdinalIgnoreCase))
            return Attach(new EventsRequest(), flags);

        if (verb.Equals("reload", StringComparison.OrdinalIgnoreCase))
            return Attach(new ReloadRequest(), flags);

        return new CommandRejected("error: unknown request");
    }

    private static CommandParse Attach(CommandRequest request, CommandFlags flags) =>
        new CommandParsed(request with { Flags = flags });

    private static CommandParse ParsePlugin(string rest)
    {
        if (rest.Equals("list", StringComparison.OrdinalIgnoreCase))
            return new CommandParsed(new PluginListRequest());

        if (TryVerb(rest, "load", out var path))
        {
            if (path.Length == 0)
                return new CommandRejected("error: missing plugin path");

            return new CommandParsed(new PluginLoadRequest(path));
        }

        if (TryVerb(rest, "unload", out var name))
        {
            if (name.Length == 0)
                return new CommandRejected("error: missing plugin name");

            return new CommandParsed(new PluginUnloadRequest(name));
        }

        return new CommandRejected("error: unknown request");
    }

    private static bool TryVerb(string rest, string verb, out string args)
    {
        if (rest.Equals(verb, StringComparison.OrdinalIgnoreCase))
        {
            args = "";
            return true;
        }

        if (rest.Length > verb.Length
            && rest.StartsWith(verb, StringComparison.OrdinalIgnoreCase)
            && char.IsWhiteSpace(rest[verb.Length]))
        {
            args = rest[verb.Length..].TrimStart();
            return true;
        }

        args = "";
        return false;
    }

    public static IReadOnlyList<string> FormatStream(IReadOnlyList<StreamEvent> events, State state)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(state);
        if (events.Count == 0)
            return [];

        var indexed = Index(state);
        var lines = new List<string>(events.Count);
        foreach (var streamEvent in events)
            Append(lines, streamEvent, state, indexed);

        return lines;
    }

    private static readonly string[] NoStrings = [];

    private static readonly WorkspaceJson NoSpecial = new(0, "");

    internal static string FormatClients(State state, bool includeUnmapped = false)
    {
        var indexed = Index(state);
        var views = new List<ClientJson>(state.Clients.Count);
        for (var i = 0; i < state.Clients.Count; i++)
        {
            var client = state.Clients[i];
            if (!includeUnmapped && client.Minimized)
                continue;

            views.Add(ClientOf(client, state, indexed));
        }

        return JsonSerializer.Serialize(views);
    }

    internal static string FormatWorkspaces(State state) =>
        JsonSerializer.Serialize(Index(state).Workspaces);

    internal static string FormatActiveWorkspace(State state)
    {
        var indexed = Index(state);
        var active = indexed.ByWorkspace[state.ActiveWorkspace];
        foreach (var entry in indexed.Workspaces)
        {
            if (entry.Id == active.Id)
                return JsonSerializer.Serialize(entry);
        }

        return "{}";
    }

    internal static string FormatMonitors(State state, bool includeDisabled = false)
    {
        var indexed = Index(state);
        if (state.Screens.Count == 0)
        {
            var active = indexed.ByWorkspace[state.ActiveWorkspace];
            var synthetic = new Monitor("", "", 0, 0, 0, 0, 1, true, default);
            return JsonSerializer.Serialize<MonitorJson[]>([
                DescribeMonitor(state.ActiveMonitor.Value, synthetic, true, active, SpecialJson(state.SpecialWorkspace, indexed)),
            ]);
        }

        var views = new List<MonitorJson>(state.Screens.Count);
        foreach (var screen in state.Screens)
        {
            if (!includeDisabled && !screen.Monitor.Enabled)
                continue;

            views.Add(DescribeMonitor(
                screen.Id.Value,
                screen.Monitor,
                screen.Focused,
                indexed.ByWorkspace[screen.ActiveWorkspace],
                SpecialJson(screen.SpecialWorkspace, indexed)));
        }

        return JsonSerializer.Serialize(views);
    }

    internal static string FormatActiveWindow(State state, WindowId? active)
    {
        if (active is not WindowId window)
            return "{}";

        var indexed = Index(state);
        foreach (var client in state.Clients)
        {
            if (client.Window == window)
                return JsonSerializer.Serialize(ClientOf(client, state, indexed));
        }

        return "{}";
    }

    private static ClientJson ClientOf(Client client, State state, Indexed indexed)
    {
        var hidden = client.Minimized || ListedHidden(state, client.Window);
        var shown = !hidden;
        return new(
            Address(client.Window),
            shown,
            hidden,
            shown,
            false,
            [client.Bounds.X, client.Bounds.Y],
            [client.Bounds.Width, client.Bounds.Height],
            indexed.ByWorkspace[client.Workspace],
            client.Floating,
            MonitorOf(state, client.Workspace),
            client.Class,
            client.Title,
            client.InitialClass.Length == 0 ? client.Class : client.InitialClass,
            client.InitialTitle.Length == 0 ? client.Title : client.InitialTitle,
            client.ProcessId,
            false,
            client.Pinned,
            false,
            (int)client.Fullscreen,
            (int)client.FullscreenClient,
            client.Fullscreen == FullscreenMode.None ? "" : "default",
            false,
            NoStrings,
            NoStrings,
            "0x0",
            0,
            false,
            "",
            "",
            "none",
            false,
            client.Id.Value.ToString("x", CultureInfo.InvariantCulture));
    }

    private static WorkspaceJson SpecialJson(WorkspaceId? workspace, Indexed indexed) =>
        workspace is WorkspaceId id && indexed.ByWorkspace.TryGetValue(id, out var described) ? described : NoSpecial;

    private static MonitorJson DescribeMonitor(int id, Monitor monitor, bool focused, WorkspaceJson active, WorkspaceJson special) =>
        new(
            id,
            monitor.Name,
            monitor.Description,
            monitor.Make,
            monitor.Model,
            monitor.Serial,
            monitor.Width,
            monitor.Height,
            monitor.PhysicalWidth,
            monitor.PhysicalHeight,
            monitor.RefreshRate,
            monitor.X,
            monitor.Y,
            active,
            special,
            Reserved(monitor),
            monitor.Scale,
            0,
            focused,
            monitor.Enabled,
            false,
            "0",
            null,
            false,
            null,
            "0",
            null,
            !monitor.Enabled,
            "",
            "none",
            Modes(monitor),
            "",
            0,
            0,
            0,
            0,
            false);

    private static int[] Reserved(Monitor monitor)
    {
        if (monitor.Width == 0 && monitor.Height == 0)
            return [0, 0, 0, 0];

        var work = monitor.WorkArea;
        return
        [
            work.X - monitor.X,
            work.Y - monitor.Y,
            monitor.X + monitor.Width - (work.X + work.Width),
            monitor.Y + monitor.Height - (work.Y + work.Height),
        ];
    }

    private static string[] Modes(Monitor monitor)
    {
        if (monitor.Width <= 0 || monitor.Height <= 0 || !(monitor.RefreshRate > 0) || !double.IsFinite(monitor.RefreshRate))
            return NoStrings;

        return [FormattableString.Invariant($"{monitor.Width}x{monitor.Height}@{monitor.RefreshRate:0.00}Hz")];
    }

    private static void Append(List<string> lines, StreamEvent streamEvent, State state, Indexed indexed)
    {
        switch (streamEvent)
        {
            case ClientOpened opened:
                lines.Add($"openwindow>>{EventAddress(opened.Window)},{WorkspaceNameText(opened.Workspace, indexed)},{Clean(opened.Class)},{Clean(opened.Title)}");
                break;
            case ClientClosed closed:
                lines.Add($"closewindow>>{EventAddress(closed.Window)}");
                break;
            case FocusChanged focused:
                AppendFocus(lines, focused, state);
                break;
            case ClientUrgent urgent:
                lines.Add("urgent>>" + EventAddress(urgent.Window));
                break;
            case ClientMoved moved:
                lines.Add($"movewindow>>{EventAddress(moved.Window)},{WorkspaceNameText(moved.Workspace, indexed)}");
                lines.Add($"movewindowv2>>{EventAddress(moved.Window)},{WorkspaceIdText(moved.Workspace, indexed)},{WorkspaceNameText(moved.Workspace, indexed)}");
                break;
            case FloatingChanged floating:
                lines.Add($"changefloatingmode>>{EventAddress(floating.Window)},{(floating.Floating ? "1" : "0")}");
                break;
            case ClientMinimized minimized:
                lines.Add($"minimized>>{EventAddress(minimized.Window)},{(minimized.Minimized ? "1" : "0")}");
                break;
            case ClientPinned pinned:
                lines.Add($"pin>>{EventAddress(pinned.Window)},{(pinned.Pinned ? "1" : "0")}");
                break;
            case WorkspaceActivated activated:
                lines.Add("workspace>>" + WorkspaceNameText(activated.Workspace, indexed));
                lines.Add($"workspacev2>>{WorkspaceIdText(activated.Workspace, indexed)},{WorkspaceNameText(activated.Workspace, indexed)}");
                break;
            case WorkspaceCreated created:
                lines.Add("createworkspace>>" + WorkspaceNameText(created.Workspace, indexed));
                lines.Add($"createworkspacev2>>{WorkspaceIdText(created.Workspace, indexed)},{WorkspaceNameText(created.Workspace, indexed)}");
                break;
            case WorkspaceDestroyed destroyed:
                lines.Add("destroyworkspace>>" + WorkspaceNameText(destroyed.Workspace, indexed));
                lines.Add($"destroyworkspacev2>>{destroyed.Id.ToString(CultureInfo.InvariantCulture)},{WorkspaceNameText(destroyed.Workspace, indexed)}");
                break;
            case WorkspaceMoved movedWorkspace:
                lines.Add($"moveworkspace>>{WorkspaceNameText(movedWorkspace.Workspace, indexed)},{Clean(movedWorkspace.Monitor)}");
                lines.Add($"moveworkspacev2>>{WorkspaceIdText(movedWorkspace.Workspace, indexed)},{WorkspaceNameText(movedWorkspace.Workspace, indexed)},{Clean(movedWorkspace.Monitor)}");
                break;
            case MonitorFocused focusedMonitor:
                lines.Add($"focusedmon>>{Clean(focusedMonitor.Name)},{WorkspaceNameText(focusedMonitor.Workspace, indexed)}");
                lines.Add($"focusedmonv2>>{Clean(focusedMonitor.Name)},{WorkspaceIdText(focusedMonitor.Workspace, indexed)}");
                break;
            case FullscreenChanged fullscreen:
                lines.Add("fullscreen>>" + (fullscreen.Entered ? "1" : "0"));
                break;
            case ActiveSpecial special:
                if (special.Workspace is WorkspaceId specialWorkspace)
                {
                    lines.Add($"activespecial>>{Clean(WorkspaceNameText(specialWorkspace, indexed))},{Clean(special.Monitor)}");
                    lines.Add($"activespecialv2>>{WorkspaceIdText(specialWorkspace, indexed)},{Clean(WorkspaceNameText(specialWorkspace, indexed))},{Clean(special.Monitor)}");
                }
                else
                {
                    lines.Add($"activespecial>>,{Clean(special.Monitor)}");
                    lines.Add($"activespecialv2>>,,{Clean(special.Monitor)}");
                }
                break;
            case ConfigWarned warned:
                lines.Add($"warning>>{warned.Line.ToString(CultureInfo.InvariantCulture)},{Clean(warned.Source)},{Clean(warned.Message)}");
                break;
            case ConfigReloaded:
                lines.Add("configreloaded>>");
                break;
            case SubmapChanged changed:
                lines.Add("submap>>" + Clean(changed.Name));
                break;
            case MonitorAdded added:
                lines.Add($"monitoradded>>{Clean(added.Name)}");
                break;
            case MonitorAddedV2 added:
                lines.Add($"monitoraddedv2>>{added.Id.ToString(CultureInfo.InvariantCulture)},{Clean(added.Name)},{Clean(added.Description)}");
                break;
            case MonitorRemoved removed:
                lines.Add($"monitorremoved>>{Clean(removed.Name)}");
                break;
            case MonitorRemovedV2 removed:
                lines.Add($"monitorremovedv2>>{removed.Id.ToString(CultureInfo.InvariantCulture)},{Clean(removed.Name)},{Clean(removed.Description)}");
                break;
            case CustomEvent custom:
                lines.Add("custom>>" + Clean(custom.Text));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(streamEvent), streamEvent, "No format for this event.");
        }
    }

    private static void AppendFocus(List<string> lines, FocusChanged focused, State state)
    {
        if (focused.Window is not WindowId window)
        {
            lines.Add("activewindow>>,");
            lines.Add("activewindowv2>>");
            return;
        }

        var klass = "";
        var title = "";
        foreach (var client in state.Clients)
        {
            if (client.Window != window)
                continue;

            klass = client.Class;
            title = client.Title;
            break;
        }

        lines.Add($"activewindow>>{Clean(klass)},{Clean(title)}");
        lines.Add("activewindowv2>>" + EventAddress(window));
    }

    internal static int WorkspaceNumber(State state, WorkspaceId id)
    {
        ArgumentNullException.ThrowIfNull(state);
        var indexed = Index(state);
        if (indexed.ByWorkspace.TryGetValue(id, out var described))
            return described.Id;

        if (id.TryGetNumber(out var number))
            return number;

        return 0;
    }

    private static string WorkspaceIdText(WorkspaceId id, Indexed indexed)
    {
        if (indexed.ByWorkspace.TryGetValue(id, out var described))
            return described.Id.ToString(CultureInfo.InvariantCulture);

        if (id.TryGetNumber(out var number))
            return number.ToString(CultureInfo.InvariantCulture);

        return "0";
    }

    private static string WorkspaceNameText(WorkspaceId id, Indexed indexed) =>
        indexed.ByWorkspace.TryGetValue(id, out var described) ? described.Name : id.Name;

    private static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        return text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
    }

    private static bool ListedHidden(State state, WindowId window)
    {
        foreach (var client in state.Hidden)
        {
            if (client.Window == window)
                return true;
        }

        return false;
    }

    internal static string EventAddress(WindowId window) =>
        window.Value.ToString("x", CultureInfo.InvariantCulture);

    internal static string Address(WindowId window) =>
        "0x" + EventAddress(window);

    private static Indexed Index(State state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var ids = new List<WorkspaceId>();
        var seen = new HashSet<WorkspaceId>();
        Add(state.ActiveWorkspace);
        foreach (var workspace in state.Workspaces)
            Add(workspace.Id);

        foreach (var client in state.Clients)
            Add(client.Workspace);

        foreach (var screen in state.Screens)
        {
            Add(screen.ActiveWorkspace);
            if (screen.SpecialWorkspace is WorkspaceId special)
                Add(special);
        }

        if (state.SpecialWorkspace is WorkspaceId focusedSpecial)
            Add(focusedSpecial);

        // Named workspaces have no numeric id, so they are reported as -1, -2, ... in name order.
        // The unnamed special workspace is -99. Further special workspaces continue -98, -97, ...
        var named = new List<string>();
        var specials = new List<string>();
        foreach (var id in ids)
        {
            if (id.IsSpecial)
                specials.Add(id.Name);
            else if (!id.TryGetNumber(out _))
                named.Add(id.Name);
        }

        named.Sort(StringComparer.Ordinal);
        var namedIds = new Dictionary<string, int>(named.Count, StringComparer.Ordinal);
        var next = -1;
        foreach (var name in named)
            namedIds[name] = next--;

        specials.Sort(StringComparer.Ordinal);
        var specialIds = new Dictionary<string, int>(specials.Count, StringComparer.Ordinal);
        var nextSpecial = WorkspaceId.SpecialStart + 1;
        foreach (var name in specials)
        {
            if (name == WorkspaceId.DefaultSpecial.Name)
            {
                specialIds[name] = WorkspaceId.SpecialStart;
                continue;
            }

            specialIds[name] = nextSpecial;
            nextSpecial++;
        }

        var windows = new Dictionary<WorkspaceId, int>();
        var fullscreen = new HashSet<WorkspaceId>();
        foreach (var client in state.Clients)
        {
            windows[client.Workspace] = windows.GetValueOrDefault(client.Workspace) + 1;
            if (client.Fullscreen != FullscreenMode.None)
                fullscreen.Add(client.Workspace);
        }

        var lastWindows = new Dictionary<WorkspaceId, WindowId?>();
        foreach (var workspace in state.Workspaces)
            lastWindows[workspace.Id] = workspace.LastWindow;

        var names = new Dictionary<WorkspaceId, string>();
        var persistent = new Dictionary<WorkspaceId, bool>();
        foreach (var workspace in state.Workspaces)
        {
            names[workspace.Id] = workspace.DisplayName ?? workspace.Id.Name;
            persistent[workspace.Id] = workspace.Persistent;
        }

        var byWorkspace = new Dictionary<WorkspaceId, WorkspaceJson>(ids.Count);
        var counts = new List<WorkspaceEntryJson>(ids.Count);
        foreach (var id in ids)
        {
            var json = Describe(id, namedIds, specialIds, names);
            byWorkspace[id] = json;
            var lastAddress = "0x0";
            var lastTitle = "";
            if (lastWindows.TryGetValue(id, out var last) && last is WindowId window)
            {
                lastAddress = Address(window);
                lastTitle = TitleOf(state, window);
            }

            counts.Add(new WorkspaceEntryJson(
                json.Id,
                json.Name,
                MonitorName(state, id),
                MonitorOf(state, id),
                windows.GetValueOrDefault(id),
                fullscreen.Contains(id),
                lastAddress,
                lastTitle,
                persistent.GetValueOrDefault(id),
                "dwindle"));
        }

        counts.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        return new Indexed(byWorkspace, counts);

        void Add(WorkspaceId id)
        {
            if (seen.Add(id))
                ids.Add(id);
        }
    }

    private static int MonitorOf(State state, WorkspaceId id)
    {
        foreach (var workspace in state.Workspaces)
        {
            if (workspace.Id == id)
                return workspace.Monitor.Value;
        }

        return state.ActiveMonitor.Value;
    }

    private static string MonitorName(State state, WorkspaceId id)
    {
        MonitorId monitor = state.ActiveMonitor;
        var found = false;
        foreach (var workspace in state.Workspaces)
        {
            if (workspace.Id != id)
                continue;

            monitor = workspace.Monitor;
            found = true;
            break;
        }

        if (!found)
            monitor = state.ActiveMonitor;

        foreach (var screen in state.Screens)
        {
            if (screen.Id == monitor)
                return screen.Monitor.Name;
        }

        return "";
    }

    private static string TitleOf(State state, WindowId window)
    {
        foreach (var client in state.Clients)
        {
            if (client.Window == window)
                return client.Title;
        }

        return "";
    }

    private static WorkspaceJson Describe(
        WorkspaceId id,
        Dictionary<string, int> namedIds,
        Dictionary<string, int> specialIds,
        Dictionary<WorkspaceId, string> names)
    {
        var name = names.TryGetValue(id, out var display) ? display : id.Name;
        if (id.IsSpecial)
            return new WorkspaceJson(specialIds[id.Name], name);

        if (id.TryGetNumber(out var number))
            return new WorkspaceJson(number, name);

        return new WorkspaceJson(namedIds[id.Name], name);
    }

    private static int IndexOfWhitespace(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                return i;
        }

        return -1;
    }

    private sealed record Indexed(Dictionary<WorkspaceId, WorkspaceJson> ByWorkspace, List<WorkspaceEntryJson> Workspaces);

    private sealed record ClientJson(
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("mapped")] bool Mapped,
        [property: JsonPropertyName("hidden")] bool Hidden,
        [property: JsonPropertyName("visible")] bool Visible,
        [property: JsonPropertyName("acceptsInput")] bool AcceptsInput,
        [property: JsonPropertyName("at")] int[] At,
        [property: JsonPropertyName("size")] int[] Size,
        [property: JsonPropertyName("workspace")] WorkspaceJson Workspace,
        [property: JsonPropertyName("floating")] bool Floating,
        [property: JsonPropertyName("monitor")] int Monitor,
        [property: JsonPropertyName("class")] string Class,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("initialClass")] string InitialClass,
        [property: JsonPropertyName("initialTitle")] string InitialTitle,
        [property: JsonPropertyName("pid")] long Pid,
        [property: JsonPropertyName("xwayland")] bool Xwayland,
        [property: JsonPropertyName("pinned")] bool Pinned,
        [property: JsonPropertyName("pinFullscreened")] bool PinFullscreened,
        [property: JsonPropertyName("fullscreen")] int Fullscreen,
        [property: JsonPropertyName("fullscreenClient")] int FullscreenClient,
        [property: JsonPropertyName("fullscreenHandler")] string FullscreenHandler,
        [property: JsonPropertyName("allowedOverFullscreen")] bool AllowedOverFullscreen,
        [property: JsonPropertyName("grouped")] string[] Grouped,
        [property: JsonPropertyName("tags")] string[] Tags,
        [property: JsonPropertyName("swallowing")] string Swallowing,
        [property: JsonPropertyName("focusHistoryID")] int FocusHistoryId,
        [property: JsonPropertyName("inhibitingIdle")] bool InhibitingIdle,
        [property: JsonPropertyName("xdgTag")] string XdgTag,
        [property: JsonPropertyName("xdgDescription")] string XdgDescription,
        [property: JsonPropertyName("contentType")] string ContentType,
        [property: JsonPropertyName("tearingHint")] bool TearingHint,
        [property: JsonPropertyName("stableId")] string StableId);

    private sealed record WorkspaceJson(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string Name);

    private sealed record WorkspaceEntryJson(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("monitor")] string Monitor,
        [property: JsonPropertyName("monitorID")] int MonitorId,
        [property: JsonPropertyName("windows")] int Windows,
        [property: JsonPropertyName("hasfullscreen")] bool HasFullscreen,
        [property: JsonPropertyName("lastwindow")] string LastWindow,
        [property: JsonPropertyName("lastwindowtitle")] string LastWindowTitle,
        [property: JsonPropertyName("ispersistent")] bool IsPersistent,
        [property: JsonPropertyName("tiledLayout")] string TiledLayout);

    private sealed record MonitorJson(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("make")] string Make,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("serial")] string Serial,
        [property: JsonPropertyName("width")] int Width,
        [property: JsonPropertyName("height")] int Height,
        [property: JsonPropertyName("physicalWidth")] int PhysicalWidth,
        [property: JsonPropertyName("physicalHeight")] int PhysicalHeight,
        [property: JsonPropertyName("refreshRate")] double RefreshRate,
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y,
        [property: JsonPropertyName("activeWorkspace")] WorkspaceJson ActiveWorkspace,
        [property: JsonPropertyName("specialWorkspace")] WorkspaceJson SpecialWorkspace,
        [property: JsonPropertyName("reserved")] int[] Reserved,
        [property: JsonPropertyName("scale")] double Scale,
        [property: JsonPropertyName("transform")] int Transform,
        [property: JsonPropertyName("focused")] bool Focused,
        [property: JsonPropertyName("dpmsStatus")] bool DpmsStatus,
        [property: JsonPropertyName("vrr")] bool Vrr,
        [property: JsonPropertyName("solitary")] string Solitary,
        [property: JsonPropertyName("solitaryBlockedBy")] string[]? SolitaryBlockedBy,
        [property: JsonPropertyName("activelyTearing")] bool ActivelyTearing,
        [property: JsonPropertyName("tearingBlockedBy")] string[]? TearingBlockedBy,
        [property: JsonPropertyName("directScanoutTo")] string DirectScanoutTo,
        [property: JsonPropertyName("directScanoutBlockedBy")] string[]? DirectScanoutBlockedBy,
        [property: JsonPropertyName("disabled")] bool Disabled,
        [property: JsonPropertyName("currentFormat")] string CurrentFormat,
        [property: JsonPropertyName("mirrorOf")] string MirrorOf,
        [property: JsonPropertyName("availableModes")] string[] AvailableModes,
        [property: JsonPropertyName("colorManagementPreset")] string ColorManagementPreset,
        [property: JsonPropertyName("sdrBrightness")] double SdrBrightness,
        [property: JsonPropertyName("sdrSaturation")] double SdrSaturation,
        [property: JsonPropertyName("sdrMinLuminance")] double SdrMinLuminance,
        [property: JsonPropertyName("sdrMaxLuminance")] int SdrMaxLuminance,
        [property: JsonPropertyName("hardwareCursorsInUse")] bool HardwareCursorsInUse);
}

public abstract record CommandParse;

public sealed record CommandParsed(CommandRequest Request) : CommandParse;

public sealed record CommandRejected(string Error) : CommandParse;

public abstract record CommandRequest
{
    public bool AllowsElevation => false;

    public CommandFlags Flags { get; init; }
}

public sealed record DispatchRequest(string Name, string Arguments) : CommandRequest;

public sealed record ClientsRequest(bool All = false) : CommandRequest;

public sealed record WorkspacesRequest : CommandRequest;

public sealed record WorkspaceRulesRequest : CommandRequest;

public sealed record MonitorsRequest(bool All = false) : CommandRequest;

public sealed record ActiveWorkspaceRequest : CommandRequest;

public sealed record ActiveWindowRequest : CommandRequest;

public sealed record EventsRequest : CommandRequest;

public sealed record ReloadRequest : CommandRequest;

public abstract record PluginRequest : CommandRequest;

public sealed record PluginListRequest : PluginRequest;

public sealed record PluginLoadRequest(string AssemblyPath) : PluginRequest;

public sealed record PluginUnloadRequest(string Name) : PluginRequest;

public readonly record struct CommandReply(string Text, Outcome Effect)
{
    public bool Stream { get; init; }
}
