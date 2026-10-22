namespace Winland.Application.Tests;

/// <summary>A config source that reads files from a dictionary, by the path a <c>source</c> line names.</summary>
internal sealed class MemorySource(IReadOnlyDictionary<string, string> files) : IConfigSource
{
    public string? Read(string path) => files.TryGetValue(path, out var text) ? text : null;
}
