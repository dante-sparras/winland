namespace Winland.Domain;

internal readonly record struct LaunchParse(Launch? Launch, IReadOnlyList<string> Warnings);
