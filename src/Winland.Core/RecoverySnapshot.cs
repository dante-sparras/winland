using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winland;

/// <summary>
/// What the core recorded about one Client for the next start. <see cref="Floating"/>, <see cref="Bounds"/>,
/// <see cref="Pinned"/> and the fullscreen modes are null in a snapshot written before they were recorded,
/// and the window rules decide them then.
/// </summary>
public sealed record RecoveredClient(
    WindowId Window,
    WorkspaceId Workspace,
    MonitorId Monitor,
    bool Hidden,
    string Class,
    string Title,
    long ProcessId,
    bool Layered = false,
    bool? Floating = null,
    Rectangle? Bounds = null,
    bool? Pinned = null,
    FullscreenMode? Fullscreen = null,
    FullscreenMode? FullscreenClient = null)
{
    /// <summary>The style the window had before fullscreen mode 2 dropped its frame. Null when the frame is its own.</summary>
    public uint? FrameStyle { get; init; }

    /// <summary>True when the window's region is the corner outline Winland cut, not one of its own.</summary>
    public bool Region { get; init; }
}

public sealed record RecoverySnapshot(IReadOnlyList<RecoveredClient> Clients)
{
    public InputOriginals? Input { get; init; }

    /// <summary>The displays Winland made mirror another, to put back when it starts again after it did not stop cleanly.</summary>
    public IReadOnlyList<DisplayMirrorOriginal>? Mirrors { get; init; }

    /// <summary>Graphics preferences Winland changed, to put back when it starts again after it did not stop cleanly.</summary>
    public VrrOriginals? Vrr { get; init; }
    /// <summary>The color switches Winland changed, to put back when it starts again after it did not stop cleanly.</summary>
    public IReadOnlyList<DisplayColorOriginal>? Colors { get; init; }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson()
    {
        var rows = new ClientBody[Clients.Count];
        for (var i = 0; i < Clients.Count; i++)
        {
            var client = Clients[i];
            rows[i] = new ClientBody(
                client.Window.Value,
                client.Workspace.Name,
                client.Monitor.Value,
                client.Hidden,
                client.Class,
                client.Title,
                client.ProcessId,
                client.Layered,
                client.Floating,
                BoundsBody.From(client.Bounds),
                client.Pinned,
                client.Fullscreen is FullscreenMode internalMode ? (int)internalMode : null,
                client.FullscreenClient is FullscreenMode clientMode ? (int)clientMode : null,
                client.FrameStyle,
                client.Region);
        }

        return JsonSerializer.Serialize(new FileBody(rows, InputBody.From(Input), MirrorBody.From(Mirrors), VrrBody.From(Vrr), ColorBody.From(Colors)), Json);
    }

    public static bool TryParse(string? json, out RecoverySnapshot snapshot)
    {
        snapshot = new([]);
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            var body = JsonSerializer.Deserialize<FileBody>(json, Json);
            if (body?.Clients is null)
                return false;

            var clients = new List<RecoveredClient>(body.Clients.Length);
            foreach (var row in body.Clients)
            {
                if (row is null || row.Window == 0 || string.IsNullOrEmpty(row.Workspace))
                    continue;

                clients.Add(new RecoveredClient(
                    new WindowId(row.Window),
                    new WorkspaceId(row.Workspace),
                    new MonitorId(row.Monitor),
                    row.Hidden,
                    row.WindowClass ?? "",
                    row.Title ?? "",
                    row.Process,
                    row.Layered,
                    row.Floating,
                    row.Bounds?.ToRectangle(),
                    row.Pinned,
                    Mode(row.Fullscreen),
                    Mode(row.FullscreenClient))
                {
                    FrameStyle = row.FrameStyle,
                    Region = row.Region,
                });
            }

            snapshot = new RecoverySnapshot(clients)
            {
                Input = body.Input?.ToOriginals(),
                Mirrors = MirrorBody.ToOriginals(body.Mirrors),
                Vrr = body.Vrr?.ToOriginals(),
                Colors = ColorBody.ToOriginals(body.Colors),
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static FullscreenMode? Mode(int? value) =>
        value is int mode && Enum.IsDefined((FullscreenMode)mode) ? (FullscreenMode)mode : null;

    private sealed record FileBody(
        [property: JsonPropertyName("clients")] ClientBody?[]? Clients,
        [property: JsonPropertyName("input")] InputBody? Input,
        [property: JsonPropertyName("mirrors")] MirrorBody?[]? Mirrors = null,
        [property: JsonPropertyName("vrr")] VrrBody? Vrr = null,
        [property: JsonPropertyName("colors")] ColorBody?[]? Colors = null);

    private sealed record ColorBody(
        [property: JsonPropertyName("device")] string? Device,
        [property: JsonPropertyName("hdr")] bool Hdr,
        [property: JsonPropertyName("wide")] bool Wide,
        [property: JsonPropertyName("split")] bool Split)
    {
        public static ColorBody[]? From(IReadOnlyList<DisplayColorOriginal>? colors)
        {
            if (colors is not { Count: > 0 })
                return null;

            var rows = new ColorBody[colors.Count];
            for (var i = 0; i < colors.Count; i++)
            {
                var color = colors[i];
                rows[i] = new ColorBody(color.Device, color.Hdr, color.Wide, color.Split);
            }

            return rows;
        }

        public static IReadOnlyList<DisplayColorOriginal>? ToOriginals(ColorBody?[]? rows)
        {
            if (rows is null)
                return null;

            var colors = new List<DisplayColorOriginal>(rows.Length);
            foreach (var row in rows)
            {
                if (row is null || string.IsNullOrEmpty(row.Device))
                    continue;

                colors.Add(new DisplayColorOriginal(row.Device, row.Hdr, row.Wide, row.Split));
            }

            return colors.Count == 0 ? null : colors;
        }
    }

    private sealed record MirrorBody(
        [property: JsonPropertyName("device")] string? Device,
        [property: JsonPropertyName("source")] string? Source,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("adapter")] long Adapter,
        [property: JsonPropertyName("target")] uint Target,
        [property: JsonPropertyName("sourceId")] uint SourceId,
        [property: JsonPropertyName("mode")] SourceModeBody? Mode)
    {
        public static MirrorBody[]? From(IReadOnlyList<DisplayMirrorOriginal>? mirrors)
        {
            if (mirrors is not { Count: > 0 })
                return null;

            var rows = new MirrorBody[mirrors.Count];
            for (var i = 0; i < mirrors.Count; i++)
            {
                var mirror = mirrors[i];
                rows[i] = new MirrorBody(
                    mirror.Device,
                    mirror.Source,
                    mirror.Description,
                    mirror.Adapter,
                    mirror.Target,
                    mirror.SourceId,
                    mirror.Mode is DisplaySourceMode mode ? new SourceModeBody(mode.Width, mode.Height, mode.PixelFormat, mode.X, mode.Y) : null);
            }

            return rows;
        }

        public static IReadOnlyList<DisplayMirrorOriginal>? ToOriginals(MirrorBody?[]? rows)
        {
            if (rows is null)
                return null;

            var mirrors = new List<DisplayMirrorOriginal>(rows.Length);
            foreach (var row in rows)
            {
                if (row is null || string.IsNullOrEmpty(row.Device))
                    continue;

                mirrors.Add(new DisplayMirrorOriginal(
                    row.Device,
                    row.Source ?? "",
                    row.Description ?? "",
                    row.Adapter,
                    row.Target,
                    row.SourceId,
                    row.Mode is SourceModeBody mode ? new DisplaySourceMode(mode.Width, mode.Height, mode.PixelFormat, mode.X, mode.Y) : null));
            }

            return mirrors.Count == 0 ? null : mirrors;
        }
    }

    private sealed record VrrBody(
        [property: JsonPropertyName("global")] bool Global,
        [property: JsonPropertyName("globalValue")] string? GlobalValue,
        [property: JsonPropertyName("executables")] VrrExecutableBody?[]? Executables)
    {
        public static VrrBody? From(VrrOriginals? originals)
        {
            if (originals is not { Any: true } saved)
                return null;

            VrrExecutableBody[]? rows = null;
            if (saved.Executables.Count > 0)
            {
                rows = new VrrExecutableBody[saved.Executables.Count];
                for (var i = 0; i < saved.Executables.Count; i++)
                {
                    var row = saved.Executables[i];
                    rows[i] = new VrrExecutableBody(row.Executable, row.Value);
                }
            }

            return new VrrBody(saved.Global, saved.GlobalValue, rows);
        }

        public VrrOriginals ToOriginals()
        {
            var rows = new List<VrrExecutableOriginal>();
            if (Executables is not null)
            {
                foreach (var row in Executables)
                {
                    if (row is null || string.IsNullOrEmpty(row.Executable))
                        continue;

                    rows.Add(new VrrExecutableOriginal(row.Executable, row.Value));
                }
            }

            return new VrrOriginals { Global = Global, GlobalValue = GlobalValue, Executables = rows };
        }
    }

    private sealed record VrrExecutableBody(
        [property: JsonPropertyName("executable")] string? Executable,
        [property: JsonPropertyName("value")] string? Value);

    private sealed record SourceModeBody(
        [property: JsonPropertyName("width")] uint Width,
        [property: JsonPropertyName("height")] uint Height,
        [property: JsonPropertyName("pixelFormat")] uint PixelFormat,
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y);

    private sealed record InputBody(
        [property: JsonPropertyName("keyboardDelay")] int? KeyboardDelay,
        [property: JsonPropertyName("keyboardSpeed")] int? KeyboardSpeed,
        [property: JsonPropertyName("numLock")] bool? NumLock,
        [property: JsonPropertyName("swappedButtons")] bool? SwappedButtons,
        [property: JsonPropertyName("mouse")] MouseBody? Mouse,
        [property: JsonPropertyName("mouseSpeed")] int? MouseSpeed,
        [property: JsonPropertyName("wheelLines")] int? WheelLines,
        [property: JsonPropertyName("touchpad")] TouchpadBody? Touchpad = null)
    {
        public static InputBody? From(InputOriginals? originals)
        {
            if (originals is not InputOriginals saved || !saved.Any)
                return null;

            MouseBody? mouse = saved.MouseCurve is MouseCurve curve
                ? new MouseBody(curve.Threshold1, curve.Threshold2, curve.Acceleration)
                : null;
            return new InputBody(
                saved.KeyboardDelay,
                saved.KeyboardSpeed,
                saved.NumLock,
                saved.SwappedButtons,
                mouse,
                saved.MouseSpeed,
                saved.WheelLines,
                saved.Touchpad is TouchpadSettings pad
                    ? new TouchpadBody(pad.Tap, pad.TapAndDrag, pad.ReverseScroll, pad.Sensitivity, pad.RightClickZone, pad.TwoFingerTap)
                    : null);
        }

        public InputOriginals ToOriginals() => new()
        {
            KeyboardDelay = KeyboardDelay,
            KeyboardSpeed = KeyboardSpeed,
            NumLock = NumLock,
            SwappedButtons = SwappedButtons,
            MouseCurve = Mouse is MouseBody mouse
                ? new MouseCurve(mouse.Threshold1, mouse.Threshold2, mouse.Acceleration)
                : null,
            MouseSpeed = MouseSpeed,
            WheelLines = WheelLines,
            Touchpad = Touchpad is TouchpadBody pad
                ? new TouchpadSettings(pad.Tap, pad.TapAndDrag, pad.ReverseScroll, pad.Sensitivity, pad.RightClickZone, pad.TwoFingerTap)
                : null,
        };
    }

    private sealed record TouchpadBody(
        [property: JsonPropertyName("tap")] bool? Tap,
        [property: JsonPropertyName("tapAndDrag")] bool? TapAndDrag,
        [property: JsonPropertyName("reverseScroll")] bool? ReverseScroll,
        [property: JsonPropertyName("sensitivity")] int? Sensitivity,
        [property: JsonPropertyName("rightClickZone")] bool? RightClickZone,
        [property: JsonPropertyName("twoFingerTap")] bool? TwoFingerTap = null);

    private sealed record MouseBody(
        [property: JsonPropertyName("threshold1")] int Threshold1,
        [property: JsonPropertyName("threshold2")] int Threshold2,
        [property: JsonPropertyName("acceleration")] int Acceleration);

    private sealed record ClientBody(
        [property: JsonPropertyName("window")] long Window,
        [property: JsonPropertyName("workspace")] string? Workspace,
        [property: JsonPropertyName("monitor")] int Monitor,
        [property: JsonPropertyName("hidden")] bool Hidden,
        [property: JsonPropertyName("class")] string? WindowClass,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("process")] long Process,
        [property: JsonPropertyName("layered")] bool Layered = false,
        [property: JsonPropertyName("floating")] bool? Floating = null,
        [property: JsonPropertyName("bounds")] BoundsBody? Bounds = null,
        [property: JsonPropertyName("pinned")] bool? Pinned = null,
        [property: JsonPropertyName("fullscreen")] int? Fullscreen = null,
        [property: JsonPropertyName("fullscreenClient")] int? FullscreenClient = null,
        [property: JsonPropertyName("frameStyle")] uint? FrameStyle = null,
        [property: JsonPropertyName("region")] bool Region = false);

    private sealed record BoundsBody(
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y,
        [property: JsonPropertyName("width")] int Width,
        [property: JsonPropertyName("height")] int Height)
    {
        public static BoundsBody? From(Rectangle? bounds) =>
            bounds is Rectangle box ? new BoundsBody(box.X, box.Y, box.Width, box.Height) : null;

        public Rectangle ToRectangle() => new(X, Y, Width, Height);
    }
}
