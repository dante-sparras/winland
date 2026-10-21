using System.Text;

namespace Winland.Domain;

/// <summary>What an input device is: a pointer, such as a mouse or a touchpad, or a keyboard.</summary>
public enum InputDeviceKind
{
    Pointer,
    Keyboard,
}

/// <summary>
/// An input device as the system names it. <paramref name="Address"/> tells it apart from the others for as long as it
/// is plugged in, and <paramref name="Name"/> is its product name, which <see cref="InputDeviceNames"/> turns into the
/// name the replies give.
/// </summary>
public sealed record InputDevice(InputDeviceKind Kind, long Address, string Name);

/// <summary>Device names as Winland makes them.</summary>
public static class InputDeviceNames
{
    /// <summary>
    /// A name in lower case with a space, a line break or a comma as <c>-</c>, as Winland writes a device's
    /// name for its config and its replies.
    /// </summary>
    public static string Internal(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var result = new StringBuilder(name.Length);
        foreach (var ch in name)
            result.Append(ch is ' ' or '\n' or ',' ? '-' : char.ToLowerInvariant(ch));

        return result.ToString();
    }

    /// <summary>
    /// Each device's name, in order: its <see cref="Internal"/> name, <c>unknown-device</c> for an empty one, and
    /// <c>-1</c>, <c>-2</c> and so on after a name an earlier device already has, as Winland names a new device.
    /// </summary>
    public static IReadOnlyList<string> Assign(IReadOnlyList<InputDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var names = new string[devices.Count];
        for (var i = 0; i < devices.Count; i++)
        {
            var proposed = Internal(devices[i].Name);
            if (proposed.Length == 0)
                proposed = "unknown-device";

            var name = proposed;
            for (var duplicate = 1; taken.Contains(name); duplicate++)
                name = proposed + "-" + duplicate.ToString(System.Globalization.CultureInfo.InvariantCulture);

            taken.Add(name);
            names[i] = name;
        }

        return names;
    }
}
