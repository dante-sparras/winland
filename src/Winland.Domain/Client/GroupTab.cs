namespace Winland.Domain;

/// <summary>One member's tab. <see cref="Focused"/> marks the focused window, which the bar colors as active.</summary>
public readonly record struct GroupTab(WindowId Window, string Title, bool Focused);
