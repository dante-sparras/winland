using System.Globalization;

namespace Winland.Domain;

/// <summary>Windows disabled a Client, as it does while a modal dialog it owns is open, or enabled it again.</summary>
public sealed record WindowEnabledChanged(WindowId Window, bool Enabled) : Event;
