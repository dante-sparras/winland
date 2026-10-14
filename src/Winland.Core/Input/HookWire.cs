using System.Globalization;

namespace Winland.Core.Input;

/// <summary>
/// One line between the desktop and the keyboard hook process.
/// The host sends chords, Super's state, and when hover may focus.
/// The desktop sends switches, the Escape mode, pointer notices, and questions that carry an id.
/// A line that does not parse is dropped by the reader, so a newer peer cannot crash an older one.
/// </summary>
public abstract record HookWire
{
    private HookWire()
    {
    }

    /// <summary>Host: a chord key went down or up.</summary>
    /// <param name="Chord">The chord.</param>
    /// <param name="Down">True for key-down.</param>
    public sealed record ChordKeyed(Chord Chord, bool Down) : HookWire;

    /// <summary>Host: whether a Super finger is held.</summary>
    /// <param name="Held">True while held.</param>
    public sealed record Super(bool Held) : HookWire;

    /// <summary>Host: Escape went down while the desktop asked for it.</summary>
    public sealed record EscapeTaken : HookWire;

    /// <summary>Host: the pointer moved with no key but Super held, so hover may focus the window under it.</summary>
    public sealed record Hovered : HookWire;

    /// <summary>Host: the answer to a question with <paramref name="Id"/>.</summary>
    /// <param name="Id">The question's id.</param>
    /// <param name="Value">The answer.</param>
    public sealed record Reply(int Id, bool Value) : HookWire;

    /// <summary>Desktop: whether a plugin has claimed Escape.</summary>
    /// <param name="Cancels">True while a plugin holds the claim.</param>
    public sealed record EscapeMode(bool Cancels) : HookWire;

    /// <summary>Desktop: whether the picker is open, so the host keeps its keys.</summary>
    /// <param name="Open">True while the picker is open.</param>
    public sealed record PickerMode(bool Open) : HookWire;

    /// <summary>
    /// The highest picker-key code the wire carries. Zero is Escape and is not a picked line.
    /// The shell's picker keys use these numbers, in order, starting at zero.
    /// </summary>
    public const int LastPickerCode = 8;

    /// <summary>Host: a key of the picker's went down and was kept from the chords and the program in front.</summary>
    /// <param name="Key">The key's code, from 1 through <see cref="LastPickerCode"/>.</param>
    public sealed record PickerKeyTaken(int Key) : HookWire;

    /// <summary>Desktop: the keyboard was moved, or the window that has it was shown. Keys held off a hidden window may go out now.</summary>
    public sealed record Deliver : HookWire;

    /// <summary>Desktop: the pointer may have moved. The host reads it and sends <see cref="Hovered"/> when hover may focus.</summary>
    public sealed record Pointer : HookWire;

    /// <summary>Desktop: release every finger before the session locks. The reply says it is done.</summary>
    /// <param name="Id">The question's id.</param>
    public sealed record Release(int Id) : HookWire;

    /// <summary>Desktop: Super went up where the hook was not called, in an administrator window. Forget the press.</summary>
    public sealed record Forget : HookWire;

    /// <summary>Desktop: the desktop is closing. Release keys and exit.</summary>
    public sealed record Quit : HookWire;

    /// <summary>The line for this message, without a line break.</summary>
    /// <returns>The line.</returns>
    public string Format() => this switch
    {
        ChordKeyed keyed => "chord " + Number((int)keyed.Chord.Modifiers) + " " + Number((int)keyed.Chord.Key) + " " + Flag(keyed.Down),
        Super super => "super " + Flag(super.Held),
        EscapeTaken => "escaped",
        Hovered => "hovered",
        Reply reply => "reply " + Number(reply.Id) + " " + Flag(reply.Value),
        EscapeMode mode => "escape " + Flag(mode.Cancels),
        PickerMode mode => "picker " + Flag(mode.Open),
        PickerKeyTaken taken => "picked " + Number(taken.Key),
        Deliver => "deliver",
        Pointer => "pointer",
        Release release => "release " + Number(release.Id),
        Forget => "forget",
        Quit => "quit",
        _ => throw new InvalidOperationException("Unknown hook message."),
    };

    /// <summary>Reads one line.</summary>
    /// <param name="line">The line.</param>
    /// <param name="message">The message when this returns true.</param>
    /// <returns>False when the line is not a message this side knows.</returns>
    public static bool TryParse(string? line, out HookWire message)
    {
        message = new Quit();
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        HookWire? read = parts switch
        {
            ["chord", var modifiers, var key, var down] when Int(modifiers) is int m && Int(key) is int k && Bool(down) is bool d
                && Enum.IsDefined((ChordKey)k) => new ChordKeyed(new Chord((ChordModifiers)m, (ChordKey)k), d),
            ["super", var held] when Bool(held) is bool h => new Super(h),
            ["escaped"] => new EscapeTaken(),
            ["hovered"] => new Hovered(),
            ["reply", var id, var value] when Int(id) is int i && Bool(value) is bool v => new Reply(i, v),
            ["escape", var cancels] when Bool(cancels) is bool c => new EscapeMode(c),
            ["picker", var open] when Bool(open) is bool o => new PickerMode(o),
            ["picked", var key] when Int(key) is int p && p is >= 1 and <= LastPickerCode => new PickerKeyTaken(p),
            ["deliver"] => new Deliver(),
            ["pointer"] => new Pointer(),
            ["release", var id] when Int(id) is int i => new Release(i),
            ["forget"] => new Forget(),
            ["quit"] => new Quit(),
            _ => null,
        };
        if (read is null)
        {
            return false;
        }

        message = read;
        return true;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Flag(bool value) => value ? "1" : "0";

    private static int? Int(string text) =>
        int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static bool? Bool(string text) => text switch
    {
        "1" => true,
        "0" => false,
        _ => null,
    };
}
