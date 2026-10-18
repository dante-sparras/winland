using static Winland.OrdinalText;

namespace Winland;

internal enum FullscreenAction
{
    Toggle,
    Set,
    Unset,
}

internal readonly record struct FullscreenParse(bool Ignored, FullscreenMode Mode, FullscreenAction Action, bool LayoutAware = true);

internal readonly record struct FullscreenTarget(FullscreenMode Internal, FullscreenMode Client);

/// <summary>
/// <c>fullscreen</c> and <c>fullscreenstate</c> arguments.
/// The first <c>fullscreen</c> argument selects maximize only for <c>1</c>, <c>maximize</c>, or <c>maximized</c>.
/// Every other argument, including <c>0</c> and <c>2</c>, selects fullscreen. The second argument is <c>toggle</c>, <c>set</c>, or <c>unset</c>.
/// A last word <c>default</c> asks for the default fullscreen, as the pinned release's <c>layout_aware = false</c> does.
/// </summary>
internal static class FullscreenCommands
{
    internal const string DefaultHandler = "default";

    internal static FullscreenParse Parse(string arguments)
    {
        var tokens = Tokens(arguments);
        var mode = tokens.Length > 0 && IsMaximize(tokens[0])
            ? FullscreenMode.Maximized
            : FullscreenMode.Fullscreen;
        var layoutAware = !(tokens.Length > 1 && Is(tokens[^1], DefaultHandler));
        var words = layoutAware ? tokens.Length : tokens.Length - 1;
        if (words < 2)
            return new FullscreenParse(false, mode, FullscreenAction.Toggle, layoutAware);

        if (Is(tokens[1], "toggle"))
            return new FullscreenParse(false, mode, FullscreenAction.Toggle, layoutAware);
        if (Is(tokens[1], "set"))
            return new FullscreenParse(false, mode, FullscreenAction.Set, layoutAware);
        if (Is(tokens[1], "unset"))
            return new FullscreenParse(false, mode, FullscreenAction.Unset, layoutAware);

        return new FullscreenParse(true, mode, FullscreenAction.Toggle, layoutAware);
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
    /// The same pair as the Client already has turns both modes off. A kept mode is the
    /// current one in that comparison, as in the pinned release, so <c>-1 -1</c> turns both off.
    /// </summary>
    /// <summary>
    /// The window selector after <c>fullscreenstate INTERNAL CLIENT [ACTION]</c>, or empty for the focused Client.
    /// </summary>
    internal static string StateSelector(string arguments)
    {
        var tokens = Tokens(arguments);
        var start = StateWords(tokens, out _);
        return tokens.Length > start ? string.Join(' ', tokens[start..]) : "";
    }

    /// <summary>Whether <c>fullscreenstate</c> asks for the Layout's fullscreen: false after a word <c>default</c> past the action.</summary>
    internal static bool StateLayoutAware(string arguments)
    {
        _ = StateWords(Tokens(arguments), out var layoutAware);
        return layoutAware;
    }

    // The words before the window selector: the two modes, an action, and then <c>default</c>.
    private static int StateWords(string[] tokens, out bool layoutAware)
    {
        var start = tokens.Length > 2 && TryStateAction(tokens[2], out _) ? 3 : 2;
        layoutAware = !(tokens.Length > start && Is(tokens[start], DefaultHandler));
        return layoutAware ? start : start + 1;
    }

    /// <summary>
    /// The modes <c>fullscreenstate INTERNAL CLIENT [set|toggle|unset] [WINDOW]</c> leaves the Client in. A mode of -1
    /// is the current one. Without an action, the pair the Client already has turns both modes off, and any other pair is
    /// set. The actions are the pinned release's: set applies the pair unless the Client has it, toggle turns each mode off
    /// that already has its value and sets the other, and unset turns both off when the Client has the pair.
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
        var internalMode = internalRaw == -1 ? currentInternal : (FullscreenMode)Clamp(internalRaw);
        var clientMode = clientRaw == -1 ? currentClient : (FullscreenMode)Clamp(clientRaw);
        var atPair = internalMode == currentInternal && clientMode == currentClient;
        var current = new FullscreenTarget(currentInternal, currentClient);
        var none = new FullscreenTarget(FullscreenMode.None, FullscreenMode.None);
        if (tokens.Length <= 2 || !TryStateAction(tokens[2], out var action))
            return atPair ? none : new FullscreenTarget(internalMode, clientMode);

        return action switch
        {
            StateAction.Set => atPair ? current : new FullscreenTarget(internalMode, clientMode),
            StateAction.Unset => atPair ? none : current,
            _ => new FullscreenTarget(
                internalRaw != -1 && internalMode == currentInternal ? FullscreenMode.None : internalMode,
                clientRaw != -1 && clientMode == currentClient ? FullscreenMode.None : clientMode),
        };
    }

    private enum StateAction
    {
        Set,
        Toggle,
        Unset,
    }

    private static bool TryStateAction(string token, out StateAction action)
    {
        action = token.ToLowerInvariant() switch
        {
            "set" => StateAction.Set,
            "toggle" => StateAction.Toggle,
            "unset" => StateAction.Unset,
            _ => (StateAction)(-1),
        };
        return Enum.IsDefined(action);
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

    private static string[] Tokens(string arguments) =>
        arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
