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
    /// <summary>
    /// Windows puts every keyboard through one layout, so Winland reports one keyboard and one mouse in their place.
    /// </summary>
    public const string KeyboardName = "keyboard";

    private const string MouseName = "mouse";

    private int _layoutIndex;

    /// <summary>
    /// Whether binds read keys in the first layout <c>kb_layout</c> names, as the pinned release does unless
    /// <c>resolve_binds_by_sym</c> is set. With no <c>kb_layout</c> Winland leaves the layouts alone, and binds read keys in
    /// the layout in use. The keyboard hook reads it with no lock.
    /// </summary>
    public bool BindsInFirstLayout => !_input.ResolveBindsBySym && _input.KbLayout is not null;

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
        if (keyboard is not ("all" or "main" or "active" or "current") && !keyboard.Equals(KeyboardName, StringComparison.Ordinal))
            return new CommandReply("device not found", View([]));

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
            (_extraEvents ??= []).Add(new ActiveLayoutChanged(KeyboardName, Layouts()[next].Description));

        return new CommandReply("ok", View([]));
    }

    private CommandReply Devices(DevicesRequest request)
    {
        var layouts = Layouts();
        var index = layouts.Count == 0 ? 0 : Math.Clamp(_layoutIndex, 0, layouts.Count - 1);
        var keymap = layouts.Count == 0 ? "" : layouts[index].Description;
        var layout = _input.KbLayout ?? "us";
        var variant = _input.KbVariant ?? "";
        var text = new StringBuilder();
        if (request.Flags.HasFlag(CommandFlags.Json))
        {
            text.Append(CultureInfo.InvariantCulture, $$"""
                {
                "mice": [
                    {
                        "address": "0x1",
                        "name": "{{MouseName}}",
                        "defaultSpeed": 0.00000,
                        "scrollFactor": -1.00
                    }
                ],
                "keyboards": [
                    {
                        "address": "0x2",
                        "name": "{{KeyboardName}}",
                        "rules": "",
                        "model": "",
                        "layout": "{{Escape(layout)}}",
                        "variant": "{{Escape(variant)}}",
                        "options": "",
                        "active_layout_index": {{index}},
                        "active_keymap": "{{Escape(keymap)}}",
                        "capsLock": false,
                        "numLock": false,
                        "main": true
                    }
                ],
                "tablets": [
                ],
                "touch": [
                ],
                "switches": [
                ]
                }
                """);
            return new CommandReply(text.ToString(), View([]));
        }

        text.Append(CultureInfo.InvariantCulture, $"mice:\n\tMouse at 1:\n\t\t{MouseName}\n\t\t\tdefault speed: 0.00000\n\t\t\tscroll factor: -1.00\n");
        text.Append(CultureInfo.InvariantCulture, $"\n\nKeyboards:\n\tKeyboard at 2:\n\t\t{KeyboardName}\n");
        text.Append(CultureInfo.InvariantCulture, $"\t\t\trules: r \"\", m \"\", l \"{layout}\", v \"{variant}\", o \"\"\n");
        text.Append(CultureInfo.InvariantCulture, $"\t\t\tactive layout index: {index}\n\t\t\tactive keymap: {keymap}\n\t\t\tcapsLock: no\n\t\t\tnumLock: no\n\t\t\tmain: yes\n");
        text.Append("\n\nTablets:\n\n\nTouch:\n\n\nSwitches:\n");
        return new CommandReply(text.ToString(), View([]));
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
