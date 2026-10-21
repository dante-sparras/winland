using System.Globalization;

namespace Winland.Domain;

/// <summary>One notch of the wheel. <paramref name="Key"/> is mouse_up, mouse_down, mouse_left, or mouse_right.</summary>
public sealed record WheelTurned(string Key, long TimeMs) : Event;
