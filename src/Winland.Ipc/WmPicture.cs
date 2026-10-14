using System.Text;
using System.Text.Json;

namespace Winland.Ipc;

/// <summary>
/// The window manager's scene, as JSON the shell paints from. Written by hand: native <c>winland.exe</c> links this assembly,
/// and a reflected serializer does not. A missing field reads as empty or zero.
/// </summary>
public static class WmPicture
{
    /// <summary>One monitor, and whether the workspace in front on it is fullscreen.</summary>
    /// <param name="X">Left edge, in physical pixels.</param>
    /// <param name="Y">Top edge, in physical pixels.</param>
    /// <param name="Width">Width.</param>
    /// <param name="Height">Height.</param>
    /// <param name="Fullscreen">True when a fullscreen window covers this monitor.</param>
    public readonly record struct Screen(int X, int Y, int Width, int Height, bool Fullscreen);

    /// <summary>One window the bar and the menu list.</summary>
    /// <param name="Id">Window identity.</param>
    /// <param name="Title">Caption text. Empty when the window has none.</param>
    /// <param name="Place"><c>workspace</c>, <c>scratchpad</c>, <c>sticky</c>, or <c>picture-in-picture</c>.</param>
    /// <param name="Workspace">The workspace number, or 0 when the window is on none.</param>
    public readonly record struct Client(long Id, string Title, string Place, int Workspace);

    /// <summary>One group strip.</summary>
    /// <param name="X">Left edge.</param>
    /// <param name="Y">Top edge.</param>
    /// <param name="Width">Width.</param>
    /// <param name="Height">Height.</param>
    /// <param name="Members">The windows, in the order the strip shows them.</param>
    /// <param name="Active">The index of the window the tile shows.</param>
    public readonly record struct Strip(int X, int Y, int Width, int Height, long[] Members, int Active);

    /// <summary>The workspace picture. Occupancy and front monitors are indexed by workspace number, 0 through 9. Slot 0 is unused.</summary>
    /// <param name="Scene">Changes when the tiled set or the current workspace changes.</param>
    /// <param name="Current">The workspace in front where the keyboard is.</param>
    /// <param name="Monitor">The 0-based monitor with the keyboard.</param>
    /// <param name="Several">True when a workspace is in front on each monitor.</param>
    /// <param name="Switching">True while a workspace change is still moving windows.</param>
    /// <param name="Scratchpad">True while the scratchpad is on screen.</param>
    /// <param name="Windows">How many windows the current workspace holds.</param>
    /// <param name="Occupancy">Ten counts. Index by workspace number.</param>
    /// <param name="Front">Ten monitors. The monitor a workspace is in front on, or -1 when it is hidden.</param>
    /// <param name="Screens">Every monitor.</param>
    /// <param name="FrontX">The keyboard monitor's left edge.</param>
    /// <param name="FrontY">The keyboard monitor's top edge.</param>
    /// <param name="FrontWidth">The keyboard monitor's width.</param>
    /// <param name="FrontHeight">The keyboard monitor's height.</param>
    /// <param name="Clients">The windows the bar and the menu list.</param>
    public sealed record View(
        int Scene,
        int Current,
        int Monitor,
        bool Several,
        bool Switching,
        bool Scratchpad,
        int Windows,
        int[] Occupancy,
        int[] Front,
        Screen[] Screens,
        int FrontX,
        int FrontY,
        int FrontWidth,
        int FrontHeight,
        Client[] Clients);

    /// <summary>An empty picture: workspace 1, no windows, no monitors.</summary>
    public static View Empty { get; } = new(0, 1, 0, false, false, false, 0, new int[10], EmptyFront(), [], 0, 0, 0, 0, []);

    /// <summary>Writes <paramref name="view"/>.</summary>
    /// <param name="view">The picture.</param>
    /// <returns>One JSON object.</returns>
    public static string Write(View view)
    {
        ArgumentNullException.ThrowIfNull(view);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("scene", view.Scene);
            writer.WriteNumber("current", view.Current);
            writer.WriteNumber("monitor", view.Monitor);
            writer.WriteBoolean("several", view.Several);
            writer.WriteBoolean("switching", view.Switching);
            writer.WriteBoolean("scratchpad", view.Scratchpad);
            writer.WriteNumber("windows", view.Windows);
            WriteInts(writer, "occupancy", view.Occupancy);
            WriteInts(writer, "front", view.Front);
            writer.WriteStartArray("screens");
            foreach (var screen in view.Screens)
            {
                writer.WriteStartObject();
                writer.WriteNumber("x", screen.X);
                writer.WriteNumber("y", screen.Y);
                writer.WriteNumber("w", screen.Width);
                writer.WriteNumber("h", screen.Height);
                writer.WriteBoolean("fullscreen", screen.Fullscreen);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartObject("frontRect");
            writer.WriteNumber("x", view.FrontX);
            writer.WriteNumber("y", view.FrontY);
            writer.WriteNumber("w", view.FrontWidth);
            writer.WriteNumber("h", view.FrontHeight);
            writer.WriteEndObject();
            writer.WriteStartArray("clients");
            foreach (var client in view.Clients)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", client.Id);
                writer.WriteString("title", client.Title);
                writer.WriteString("place", client.Place);
                writer.WriteNumber("workspace", client.Workspace);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads a picture <see cref="Write"/> produced.</summary>
    /// <param name="json">One JSON object.</param>
    /// <param name="view">The picture, when the answer is true.</param>
    /// <returns>False when <paramref name="json"/> is not an object.</returns>
    public static bool TryRead(string? json, out View view)
    {
        view = Empty;
        if (!JsonLine.TryParse(json, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            var home = root.TryGetProperty("frontRect", out var rect) ? rect : default;
            view = new View(
                Int(root, "scene"),
                Int(root, "current") is var current and > 0 ? current : 1,
                Int(root, "monitor"),
                JsonLine.Flag(root, "several"),
                JsonLine.Flag(root, "switching"),
                JsonLine.Flag(root, "scratchpad"),
                Int(root, "windows"),
                Ints(root, "occupancy", 10),
                Ints(root, "front", 10, empty: -1),
                Screens(root),
                Int(home, "x"),
                Int(home, "y"),
                Int(home, "w"),
                Int(home, "h"),
                Clients(root));
            return true;
        }
    }

    /// <summary>Writes the group strips.</summary>
    /// <param name="strips">The strips. Null is none.</param>
    /// <returns>One JSON object.</returns>
    public static string WriteStrips(IReadOnlyList<Strip>? strips)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("strips");
            if (strips is not null)
            {
                foreach (var strip in strips)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("x", strip.X);
                    writer.WriteNumber("y", strip.Y);
                    writer.WriteNumber("w", strip.Width);
                    writer.WriteNumber("h", strip.Height);
                    writer.WriteStartArray("members");
                    foreach (var member in strip.Members)
                    {
                        writer.WriteNumberValue(member);
                    }

                    writer.WriteEndArray();
                    writer.WriteNumber("active", strip.Active);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads strips <see cref="WriteStrips"/> produced.</summary>
    /// <param name="json">One JSON object.</param>
    /// <param name="strips">The strips, when the answer is true.</param>
    /// <returns>False when <paramref name="json"/> is not an object.</returns>
    public static bool TryStrips(string? json, out Strip[] strips)
    {
        strips = [];
        if (!JsonLine.TryParse(json, out var document))
        {
            return false;
        }

        using (document)
        {
            strips = ReadStrips(document.RootElement);
            return true;
        }
    }

    private static int[] EmptyFront()
    {
        var front = new int[10];
        Array.Fill(front, -1);
        return front;
    }

    private static void WriteInts(Utf8JsonWriter writer, string name, int[]? values)
    {
        writer.WriteStartArray(name);
        if (values is not null)
        {
            foreach (var value in values)
            {
                writer.WriteNumberValue(value);
            }
        }

        writer.WriteEndArray();
    }

    private static int Int(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static int[] Ints(JsonElement root, string name, int length, int empty = 0)
    {
        var values = new int[length];
        if (empty != 0)
        {
            Array.Fill(values, empty);
        }

        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return values;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (index >= length)
            {
                break;
            }

            values[index++] = item.TryGetInt32(out var number) ? number : empty;
        }

        return values;
    }

    private static Screen[] Screens(JsonElement root)
    {
        if (!root.TryGetProperty("screens", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var screens = new List<Screen>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            screens.Add(new Screen(Int(item, "x"), Int(item, "y"), Int(item, "w"), Int(item, "h"), item.TryGetProperty("fullscreen", out var full) && full.ValueKind == JsonValueKind.True));
        }

        return [.. screens];
    }

    private static Client[] Clients(JsonElement root)
    {
        if (!root.TryGetProperty("clients", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var clients = new List<Client>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = item.TryGetProperty("id", out var raw) && raw.TryGetInt64(out var number) ? number : 0;
            clients.Add(new Client(id, JsonLine.Text(item, "title"), JsonLine.Text(item, "place"), Int(item, "workspace")));
        }

        return [.. clients];
    }

    private static Strip[] ReadStrips(JsonElement root)
    {
        if (!root.TryGetProperty("strips", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var strips = new List<Strip>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var members = new List<long>();
            if (item.TryGetProperty("members", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var member in list.EnumerateArray())
                {
                    if (member.TryGetInt64(out var id))
                    {
                        members.Add(id);
                    }
                }
            }

            strips.Add(new Strip(Int(item, "x"), Int(item, "y"), Int(item, "w"), Int(item, "h"), [.. members], Int(item, "active")));
        }

        return [.. strips];
    }
}
