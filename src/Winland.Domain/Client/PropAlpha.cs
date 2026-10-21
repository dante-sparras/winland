using System.Globalization;

namespace Winland.Domain;

/// <summary>One opacity fraction and whether it replaces the configured one, as a window's opacity props hold it.</summary>
internal readonly record struct PropAlpha(float Value, bool Overridden)
{
    public static PropAlpha Identity { get; } = new(1f, false);
}
