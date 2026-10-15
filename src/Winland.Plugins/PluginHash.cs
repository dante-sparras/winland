using System.Security.Cryptography;

namespace Winland.Plugins;

public static class PluginHash
{
    public static string Of(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string OfFile(string path) => Of(File.ReadAllBytes(path));
}
