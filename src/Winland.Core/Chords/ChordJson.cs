using System.Text.Json.Serialization;

namespace Winland.Core.Chords;

/// <summary>Indented camelCase for <c>bindings.json</c>.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ChordBook.ChordFile))]
internal sealed partial class ChordJson : JsonSerializerContext;
