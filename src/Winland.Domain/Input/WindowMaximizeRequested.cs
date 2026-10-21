using System.Globalization;

namespace Winland.Domain;

/// <summary>An application asked to be maximized, or to leave it. That is maximize mode 1, toggled.</summary>
public sealed record WindowMaximizeRequested(WindowId Window) : Event;
