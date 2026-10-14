using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winland.Core.Cli;

/// <summary>The desktop query the shell reads back. The shell adds the open panel, the plugins, the theme, and the capture picker.</summary>
public static class DesktopDocument
{
    /// <summary>
    /// Compact JSON for <paramref name="facts"/>, in the shape the shell's desktop snapshot reads.
    /// The plugin map is empty and no panel is open: those belong to the shell, which lays its font and idle lock over this reply.
    /// </summary>
    /// <param name="facts">The tiler's picture.</param>
    /// <returns>One JSON object.</returns>
    public static string Write(TilerFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return JsonSerializer.Serialize(new DesktopWire(facts), DesktopWireJson.Default.DesktopWire);
    }

    internal sealed class DesktopWire
    {
        public DesktopWire(TilerFacts facts)
        {
            Workspaces = facts.Workspaces;
            Windows = facts.Windows;
            Focused = facts.Focused;
            Spacing = facts.Spacing;
            IdleLock = facts.IdleLock;
            Scratchpad = facts.Scratchpad;
            Switching = facts.Switching;
            Bars = facts.Bars;
            Monitor = facts.Monitor;
            SquareTile = facts.SquareTile;
            Font = facts.Font;
        }

        public IReadOnlyList<WorkspaceFact> Workspaces { get; }

        public IReadOnlyList<WindowFact> Windows { get; }

        public long? Focused { get; }

        public bool Spacing { get; }

        public bool IdleLock { get; }

        public bool Scratchpad { get; }

        public string? Panel { get; }

        public bool Switching { get; }

        public IReadOnlyList<BarFact> Bars { get; }

        public Dictionary<string, Dictionary<string, string>> Plugins { get; } = new(StringComparer.Ordinal);

        public int Monitor { get; }

        public bool SquareTile { get; }

        public string Font { get; }

        public AppFact[] Apps { get; } = [];
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DesktopDocument.DesktopWire))]
internal sealed partial class DesktopWireJson : JsonSerializerContext;
