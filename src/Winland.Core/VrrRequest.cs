namespace Winland;

/// <summary>
/// Variable refresh rate Winland asks the account's graphics settings for. <see cref="Suppressed"/> are the
/// programs whose window has <c>no_vrr</c>. <see cref="Global"/> is <c>misc:vrr</c> when that option is 0 or 1,
/// and null when the option is absent or names a mode a graphics setting cannot follow.
/// </summary>
public sealed record VrrRequest(IReadOnlyList<string> Suppressed, bool? Global);
