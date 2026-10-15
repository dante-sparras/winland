namespace Winland;

internal enum FullscreenAction
{
    Toggle,
    Set,
    Unset,
}

internal readonly record struct FullscreenParse(bool Ignored, FullscreenMode Mode, FullscreenAction Action);

internal readonly record struct FullscreenTarget(FullscreenMode Internal, FullscreenMode Client);

/// <summary>
/// <c>fullscreen</c> and <c>fullscreenstate</c> arguments.
/// The first <c>fullscreen</c> argument selects maximize only for <c>1</c>, <c>maximize</c>, or <c>maximized</c>.
/// Every other argument, including <c>0</c> and <c>2</c>, selects fullscreen. The second argument is <c>toggle</c>, <c>set</c>, or <c>unset</c>.
/// </summary>
internal static class FullscreenCommands
{
    internal static FullscreenParse Parse(string arguments)
    {
        var tokens = Tokens(arguments);
        var mode = tokens.Length > 0 && IsMaximize(tokens[0])
            ? FullscreenMode.Maximized
            : FullscreenMode.Fullscreen;
        if (tokens.Length < 2)
            return new FullscreenParse(false, mode, FullscreenAction.Toggle);

        if (Is(tokens[1], "toggle"))
            return new FullscreenParse(false, mode, FullscreenAction.Toggle);
        if (Is(tokens[1], "set"))
            return new FullscreenParse(false, mode, FullscreenAction.Set);
        if (Is(tokens[1], "unset"))
            return new FullscreenParse(false, mode, FullscreenAction.Unset);

        return new FullscreenParse(true, mode, FullscreenAction.Toggle);
    }

    internal static bool Changed(FullscreenMode current, FullscreenMode mode, FullscreenAction action, out FullscreenMode next)
    {
        switch (action)
        {
            case FullscreenAction.Toggle:
                next = current == mode ? FullscreenMode.None : mode;
                return true;
            case FullscreenAction.Set when current != mode:
                next = mode;
                return true;
            case FullscreenAction.Unset when current == mode:
                next = FullscreenMode.None;
                return true;
            default:
                next = current;
                return false;
        }
    }

    /// <summary>
    /// <c>-1</c> keeps the current mode. Any other value is clamped to 0, 1, or 2.
    /// The same pair as the Client already has turns both modes off.
    /// </summary>
    internal static FullscreenTarget ResolveState(string arguments, FullscreenMode currentInternal, FullscreenMode currentClient)
    {
        var tokens = Tokens(arguments);
        var internalRaw = tokens.Length > 0 && int.TryParse(tokens[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedInternal)
            ? parsedInternal
            : -1;
        var clientRaw = tokens.Length > 1 && int.TryParse(tokens[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedClient)
            ? parsedClient
            : -1;
        var keepInternal = internalRaw == -1;
        var keepClient = clientRaw == -1;
        var internalMode = (FullscreenMode)Clamp(internalRaw);
        var clientMode = (FullscreenMode)Clamp(clientRaw);
        if (internalMode == currentInternal && clientMode == currentClient)
            return new FullscreenTarget(FullscreenMode.None, FullscreenMode.None);

        return new FullscreenTarget(
            keepInternal ? currentInternal : internalMode,
            keepClient ? currentClient : clientMode);
    }

    private static int Clamp(int value)
    {
        if (value < 0)
            return 0;
        if (value > 2)
            return 2;
        return value;
    }

    private static bool IsMaximize(string token) =>
        token == "1" || Is(token, "maximize") || Is(token, "maximized");

    private static bool Is(string text, string expected) =>
        text.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static string[] Tokens(string arguments) =>
        arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
