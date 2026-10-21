using System.Globalization;

namespace Winland.Domain;

/// <summary>The wheel turned over the tab bar of the Group <paramref name="Shown"/> shows, towards its next member or back.</summary>
public sealed record GroupBarScrolled(WindowId Shown, bool Forward) : Command;
