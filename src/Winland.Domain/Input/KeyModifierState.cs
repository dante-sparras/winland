using System.Globalization;

namespace Winland.Domain;

/// <summary>Modifiers the caller is tracking. Latched lock state is not an input.</summary>
public readonly record struct KeyModifierState(bool Shift, bool Control, bool Alt, bool Super);
