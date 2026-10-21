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

        return JsonSerializer.Serialize(new FileBody(rows), Json);
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

            snapshot = new RecoverySnapshot(clients);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static FullscreenMode? Mode(int? value) =>
        value is int mode && Enum.IsDefined((FullscreenMode)mode) ? (FullscreenMode)mode : null;

    private sealed record FileBody([property: JsonPropertyName("clients")] ClientBody?[]? Clients);

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
