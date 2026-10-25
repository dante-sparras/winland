using static Winland.Domain.OrdinalText;

namespace Winland.Application;

internal enum FullscreenAction
{
    Toggle,
    Set,
    Unset,
}

internal readonly record struct FullscreenParse(bool Ignored, FullscreenMode Mode, FullscreenAction Action, bool LayoutAware = true);

/// <summary>
/// <c>fullscreen</c> arguments.
/// The first <c>fullscreen</c> argument selects maximize only for <c>1</c>, <c>maximize</c>, or <c>maximized</c>.
/// Every other argument, including <c>0</c> and <c>2</c>, selects fullscreen. The second argument is <c>toggle</c>, <c>set</c>, or <c>unset</c>.
/// A last word <c>default</c> asks for the default fullscreen, as Winland's <c>layout_aware = false</c> does.
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

    private static bool IsMaximize(string token) =>
        token == "1" || Is(token, "maximize") || Is(token, "maximized");

    private static string[] Tokens(string arguments) =>
        arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
