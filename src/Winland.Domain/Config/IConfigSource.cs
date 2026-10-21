namespace Winland.Domain;

public interface IConfigSource
{
    string? Read(string path);

    IReadOnlyList<string> Match(string pattern) => [];

    /// <summary>The environment variable <paramref name="name"/>, or null when it is not set.</summary>
    string? Variable(string name) => null;
}
