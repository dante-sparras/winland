using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winland;

public sealed record RecoveredClient(
    WindowId Window,
    WorkspaceId Workspace,
    MonitorId Monitor,
    bool Hidden,
    string Class,
    string Title,
    long ProcessId,
    bool Layered = false);

public sealed record RecoverySnapshot(IReadOnlyList<RecoveredClient> Clients)
{
    public InputOriginals? Input { get; init; }

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
                client.Layered);
        }

        return JsonSerializer.Serialize(new FileBody(rows, InputBody.From(Input)), Json);
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
                    row.Layered));
            }

            snapshot = new RecoverySnapshot(clients) { Input = body.Input?.ToOriginals() };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record FileBody(
        [property: JsonPropertyName("clients")] ClientBody?[]? Clients,
        [property: JsonPropertyName("input")] InputBody? Input);

    private sealed record InputBody(
        [property: JsonPropertyName("keyboardDelay")] int? KeyboardDelay,
        [property: JsonPropertyName("keyboardSpeed")] int? KeyboardSpeed,
        [property: JsonPropertyName("numLock")] bool? NumLock,
        [property: JsonPropertyName("swappedButtons")] bool? SwappedButtons,
        [property: JsonPropertyName("mouse")] MouseBody? Mouse,
        [property: JsonPropertyName("mouseSpeed")] int? MouseSpeed,
        [property: JsonPropertyName("wheelLines")] int? WheelLines)
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
                saved.WheelLines);
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
        };
    }

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
        [property: JsonPropertyName("layered")] bool Layered = false);
}
