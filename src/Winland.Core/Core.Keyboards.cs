using System.Globalization;
using System.Text;

namespace Winland;

/// <summary>
/// The keyboard layouts <c>input:kb_layout</c> loads, by Windows layout id in config order (an empty id for a layout
/// Windows has no match for), and the index of the one to use.
/// </summary>
public sealed record KeyboardLayoutRequest(string Ids, int Active)
{
    public IReadOnlyList<string> IdList => Ids.Split(',');
}

/// <summary><c>switchxkblayout KEYBOARD next|prev|INDEX</c>.</summary>
public sealed record SwitchLayoutRequest(string Arguments) : CommandRequest;

/// <summary><c>devices</c>: the keyboards and pointers.</summary>
public sealed record DevicesRequest : CommandRequest;

/// <summary>The <c>grp:</c> key combination of <c>kb_options</c> was pressed, so the next layout is used.</summary>
public sealed record LayoutCycled : Event;

/// <summary><c>activelayout&gt;&gt;KEYBOARD,LAYOUT</c>.</summary>
public sealed record ActiveLayoutChanged(string Keyboard, string Layout) : StreamEvent;

public sealed partial class Core
{
    // The devices reported when nothing reads the system's: one keyboard and one mouse.
    private static readonly InputDevice[] StandIns =
    [
        new(InputDeviceKind.Pointer, 1, "mouse"),
        new(InputDeviceKind.Keyboard, 2, "keyboard"),
    ];

    private int _layoutIndex;

    /// <summary>
    /// Whether binds read keys in the first layout <c>kb_layout</c> names, as the pinned release does unless
    /// <c>resolve_binds_by_sym</c> is set. With no <c>kb_layout</c> Winland leaves the layouts alone, and binds read keys in
    /// the layout in use. The keyboard hook reads it with no lock.
    /// </summary>
    public bool BindsInFirstLayout => !_input.ResolveBindsBySym && _input.KbLayout is not null;

    /// <summary>The button scrolling the mouse hook carries out. It reads it with no lock.</summary>
    public ButtonScrollOptions ButtonScroll => _input.ButtonScroll;

    /// <summary>The <c>kb_options</c> the keyboard hook carries out. It reads them with no lock.</summary>
    public KeyboardOptions KeyOptions { get; private set; } = KeyboardOptions.None;

    private Outcome CycleLayout()
    {
        _ = SwitchLayout(new SwitchLayoutRequest("all next"));
        return View([]);
    }

    private IReadOnlyList<KeyboardLayoutChoice> Layouts() =>
        XkbLayouts.Parse(_input.KbLayout ?? "us", _input.KbVariant ?? "");

    // The layouts to load, only when the config names them, so an unset kb_layout leaves the system's layouts alone.
    private KeyboardLayoutRequest? LayoutRequest()
    {
        if (_input.KbLayout is null)
            return null;

        var layouts = Layouts();
        if (layouts.Count == 0)
            return null;

        var ids = string.Join(',', layouts.Select(layout => layout.WindowsId ?? ""));
        return new KeyboardLayoutRequest(ids, Math.Clamp(_layoutIndex, 0, layouts.Count - 1));
    }

    // The pinned release's switchxkblayout: all keyboards, the main one, or one by name, to the next, the previous or a
    // numbered layout. next and prev wrap around.
    private CommandReply SwitchLayout(SwitchLayoutRequest request)
    {
        var words = request.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var keyboard = words.Length > 0 ? words[0] : "";
        var command = words.Length > 1 ? words[1] : "";

        // Windows puts every keyboard through one layout, so a switch changes it for all of them. The event names the
        // keyboards the request named, as the pinned release's does.
        var keyboards = NamedDevices().Where(device => device.Device.Kind == InputDeviceKind.Keyboard).Select(device => device.Name).ToList();
        string[] named;
        if (keyboard == "all")
            named = [.. keyboards];
        else if (keyboard is "main" or "active" or "current")
            named = keyboards.Count == 0 ? [] : [keyboards[0]];
        else if (keyboards.Contains(InputDeviceNames.Internal(keyboard)))
            named = [InputDeviceNames.Internal(keyboard)];
        else
            return new CommandReply("device not found", View([]));

        if (named.Length == 0 && keyboard != "all")
            return new CommandReply("no device", View([]));

        var count = Layouts().Count;
        int next;
        if (command == "next")
        {
            next = count == 0 ? 0 : (_layoutIndex + 1) % count;
        }
        else if (command == "prev")
        {
            next = _layoutIndex == 0 ? Math.Max(0, count - 1) : _layoutIndex - 1;
        }
        else if (!int.TryParse(command, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out next))
        {
            return new CommandReply("invalid arg 2", View([]));
        }
        else if (next < 0 || next > count - 1)
        {
            return new CommandReply($"layout idx out of range of {count}", View([]));
        }

        _layoutIndex = next;
        if (count > 0)
        {
            foreach (var name in named)
                (_extraEvents ??= []).Add(new ActiveLayoutChanged(name, Layouts()[next].Description));
        }

        return new CommandReply("ok", View([]));
    }

    // The devices plugged in with the names the replies give them, or the stand-ins when nothing reads them.
    private IReadOnlyList<(InputDevice Device, string Name)> NamedDevices()
    {
        IReadOnlyList<InputDevice> devices = Devices?.Read() ?? StandIns;
        var names = InputDeviceNames.Assign(devices);
        return [.. devices.Select((device, i) => (device, names[i]))];
    }

    private CommandReply DevicesReply(DevicesRequest request)
    {
        var layouts = Layouts();
        var index = layouts.Count == 0 ? 0 : Math.Clamp(_layoutIndex, 0, layouts.Count - 1);
        var keymap = layouts.Count == 0 ? "" : layouts[index].Description;
        var layout = _input.KbLayout ?? "us";
        var variant = _input.KbVariant ?? "";
        var devices = NamedDevices();
        var mice = devices.Where(device => device.Device.Kind == InputDeviceKind.Pointer).ToList();
        var keyboards = devices.Where(device => device.Device.Kind == InputDeviceKind.Keyboard).ToList();
        var culture = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        if (request.Flags.HasFlag(CommandFlags.Json))
        {
            // Every keyboard reports the one layout Windows puts them all through, and the first is the main one.
            text.Append("{\n\"mice\": [\n");
            for (var i = 0; i < mice.Count; i++)
            {
                var (device, name) = mice[i];
                text.Append(i == 0 ? "" : ",");
                text.Append(culture, $"    {{\n        \"address\": \"0x{device.Address:x}\",\n        \"name\": \"{Escape(name)}\",\n");
                text.Append("        \"defaultSpeed\": 0.00000,\n        \"scrollFactor\": -1.00\n    }");
            }

            text.Append("\n],\n\"keyboards\": [\n");
            for (var i = 0; i < keyboards.Count; i++)
            {
                var (device, name) = keyboards[i];
                text.Append(i == 0 ? "" : ",");
                text.Append(culture, $"    {{\n        \"address\": \"0x{device.Address:x}\",\n        \"name\": \"{Escape(name)}\",\n");
                text.Append(culture, $"        \"rules\": \"\",\n        \"model\": \"\",\n        \"layout\": \"{Escape(layout)}\",\n        \"variant\": \"{Escape(variant)}\",\n");
                text.Append(culture, $"        \"options\": \"\",\n        \"active_layout_index\": {index},\n        \"active_keymap\": \"{Escape(keymap)}\",\n");
                text.Append(culture, $"        \"capsLock\": false,\n        \"numLock\": false,\n        \"main\": {(i == 0 ? "true" : "false")}\n    }}");
            }

            text.Append("\n],\n\"tablets\": [\n\n],\n\"touch\": [\n\n],\n\"switches\": [\n\n]\n}\n");
            return new CommandReply(text.ToString(), View([]));
        }

        text.Append("mice:\n");
        foreach (var (device, name) in mice)
            text.Append(culture, $"\tMouse at {device.Address:x}:\n\t\t{name}\n\t\t\tdefault speed: 0.00000\n\t\t\tscroll factor: -1.00\n");

        text.Append("\n\nKeyboards:\n");
        for (var i = 0; i < keyboards.Count; i++)
        {
            var (device, name) = keyboards[i];
            text.Append(culture, $"\tKeyboard at {device.Address:x}:\n\t\t{name}\n");
            text.Append(culture, $"\t\t\trules: r \"\", m \"\", l \"{layout}\", v \"{variant}\", o \"\"\n");
            text.Append(culture, $"\t\t\tactive layout index: {index}\n\t\t\tactive keymap: {keymap}\n\t\t\tcapsLock: no\n\t\t\tnumLock: no\n\t\t\tmain: {(i == 0 ? "yes" : "no")}\n");
        }

        text.Append("\n\nTablets:\n\n\nTouch:\n\n\nSwitches:\n");
        return new CommandReply(text.ToString(), View([]));
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
