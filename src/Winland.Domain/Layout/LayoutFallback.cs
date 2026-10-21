namespace Winland.Domain;

/// <summary>
/// The Layout a Workspace gets for a <c>general:layout</c> value, and the warning when that
/// value fell back to dwindle.
/// </summary>
internal readonly record struct LayoutFallback(Layout Layout, string? Warning);
