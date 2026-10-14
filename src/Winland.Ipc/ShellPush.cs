using System.Text.Json;

namespace Winland.Ipc;

/// <summary>
/// One key the shell wants kept while its capture picker is open. The hook matches this and does not ask the shell.
/// </summary>
/// <param name="Vk">The virtual-key code.</param>
/// <param name="Super">True when Super must be held.</param>
/// <param name="Ctrl">True when Control must be held.</param>
/// <param name="Alt">True when Alt must be held.</param>
/// <param name="Id">The number the shell understands for this key. The hook reports it and does not know the name.</param>
public readonly record struct ClaimedKey(int Vk, bool Super, bool Ctrl, bool Alt, int Id);

/// <summary>
/// The shell's state, pushed whole. Winland replaces its cache with this. The keyboard hook reads the cache and never
/// waits on a pipe.
/// </summary>
/// <param name="PickerOpen">True while the capture picker is open.</param>
/// <param name="Escape">True while a conditional bind the shell asked for is live. Dictation's Escape is that bind.</param>
/// <param name="SessionLocked">True while the Windows session is locked, or a lock has been asked for and keys must be let go.</param>
/// <param name="SurfaceHeld">True while one of the shell's windows has the keyboard.</param>
/// <param name="Keys">The keys the picker wants. Empty while the picker names none.</param>
/// <param name="BarThickness">The bar's thickness in physical pixels. Zero while the bar is hidden. The hook ignores it.</param>
/// <param name="BarEdge">The screen edge the bar occupies: <c>top</c>, <c>bottom</c>, <c>left</c>, or <c>right</c>.</param>
/// <param name="Bars">The bar window handles, the first one first. Empty while none is on screen. Null is none.</param>
/// <param name="GroupBar">Kept on the wire. Winland sizes the strip itself and does not read this.</param>
public sealed record ShellPush(
    bool PickerOpen,
    bool Escape,
    bool SessionLocked,
    bool SurfaceHeld,
    IReadOnlyList<ClaimedKey> Keys,
    int BarThickness = 0,
    string BarEdge = "top",
    IReadOnlyList<long>? Bars = null,
    int GroupBar = 0)
{
    /// <summary>Nothing open, nothing claimed, no keys, and no bar.</summary>
    public static ShellPush Closed { get; } = new(false, false, false, false, []);

    /// <summary>The line the shell sends.</summary>
    /// <returns>One JSON object.</returns>
    public string ToJson() => JsonLine.Object(
        ("kind", "state"),
        ("pickerOpen", PickerOpen),
        ("escape", Escape),
        ("sessionLocked", SessionLocked),
        ("surfaceHeld", SurfaceHeld),
        ("keys", Keys as ClaimedKey[] ?? [.. Keys]),
        ("barThickness", BarThickness),
        ("barEdge", string.IsNullOrEmpty(BarEdge) ? "top" : BarEdge),
        ("bars", Bars as long[] ?? (Bars is null ? [] : [.. Bars])),
        ("groupBar", GroupBar));

    /// <summary>Reads a <c>state</c> line.</summary>
    /// <param name="line">One JSON object.</param>
    /// <param name="push">The state, when the answer is true.</param>
    /// <returns>False when the line is not a state push.</returns>
    public static bool TryParse(string? line, out ShellPush push)
    {
        push = Closed;
        if (!JsonLine.TryParse(line, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!JsonLine.Kind(root).Equals("state", StringComparison.Ordinal))
            {
                return false;
            }

            var edge = JsonLine.Text(root, "barEdge");
            push = new ShellPush(
                JsonLine.Flag(root, "pickerOpen"),
                JsonLine.Flag(root, "escape"),
                JsonLine.Flag(root, "sessionLocked"),
                JsonLine.Flag(root, "surfaceHeld"),
                ReadKeys(root),
                Number(root, "barThickness"),
                edge.Length == 0 ? "top" : edge,
                ReadBars(root),
                Number(root, "groupBar"));
            return true;
        }
    }

    private static ClaimedKey[] ReadKeys(JsonElement root)
    {
        if (!root.TryGetProperty("keys", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var keys = new List<ClaimedKey>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var vk = item.TryGetProperty("vk", out var code) && code.TryGetInt32(out var number) ? number : 0;
            var id = item.TryGetProperty("id", out var named) && named.TryGetInt32(out var key) ? key : 0;
            keys.Add(new ClaimedKey(
                vk,
                item.TryGetProperty("super", out var super) && super.ValueKind == JsonValueKind.True,
                item.TryGetProperty("ctrl", out var ctrl) && ctrl.ValueKind == JsonValueKind.True,
                item.TryGetProperty("alt", out var alt) && alt.ValueKind == JsonValueKind.True,
                id));
        }

        return [.. keys];
    }

    private static int Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static long[] ReadBars(JsonElement root)
    {
        if (!root.TryGetProperty("bars", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var bars = new List<long>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.TryGetInt64(out var id))
            {
                bars.Add(id);
            }
        }

        return [.. bars];
    }
}
