using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winland.Plugins;

public sealed record PluginApproval(string Name, string Sha256);

internal static class ApprovalFile
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<PluginApproval> Read(string path, IPluginLog log)
    {
        if (!File.Exists(path))
            return [];

        try
        {
            var entries = JsonSerializer.Deserialize<List<ApprovalEntry?>>(File.ReadAllText(path), Json) ?? [];
            var approvals = new List<PluginApproval>(entries.Count);
            foreach (var entry in entries)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Sha256))
                    continue;

                approvals.Add(new PluginApproval(entry.Name.Trim(), entry.Sha256.Trim().ToLowerInvariant()));
            }

            return approvals;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            log.Write($"Could not read approvals '{path}': {exception.Message}");
            return [];
        }
    }

    private sealed class ApprovalEntry
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }
    }
}
