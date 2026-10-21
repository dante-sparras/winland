using System.Globalization;

namespace Winland.Domain;

/// <summary>The Windows accent. The active border follows it unless the config set <c>general:col.active_border</c>.</summary>
public sealed record AccentChanged(BorderColor Color) : Event;
