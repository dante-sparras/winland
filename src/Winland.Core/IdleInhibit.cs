namespace Winland;

/// <summary>When a Client keeps the system awake, from its <c>idle_inhibit</c> window rule.</summary>
public enum IdleInhibitMode
{
    None,
    Always,
    Focus,
    Fullscreen,
}

internal static class IdleInhibit
{
    internal static bool TryParse(string text, out IdleInhibitMode mode)
    {
        switch (text)
        {
            case "none":
                mode = IdleInhibitMode.None;
                return true;
            case "always":
                mode = IdleInhibitMode.Always;
                return true;
            case "focus":
                mode = IdleInhibitMode.Focus;
                return true;
            case "fullscreen":
                mode = IdleInhibitMode.Fullscreen;
                return true;
            default:
                mode = IdleInhibitMode.None;
                return false;
        }
    }

    /// <summary>True while any Client in the state keeps the system awake.</summary>
    public static bool Wanted(State state)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (var client in state.Clients)
        {
            if (client.InhibitingIdle)
                return true;
        }

        return false;
    }
}
