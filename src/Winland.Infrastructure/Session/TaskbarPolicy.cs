namespace Winland.Infrastructure;

/// <summary>What the host does with the taskbars when it reads <c>general:hide_taskbar</c>.</summary>
internal readonly record struct TaskbarChoice(bool Hide, bool Restore, bool Relayout);

internal static class TaskbarPolicy
{
    /// <summary>
    /// <paramref name="hide"/> is the option. <paramref name="owns"/> means this run hid the bars and has not put them
    /// back. <paramref name="marker"/> is a crash marker from a hide that did not exit. <paramref name="open"/> means
    /// the monitors are already laid out, so a change lays them out again in the work area that leaves. Hiding again
    /// is not a change: <see cref="Taskbar.Hide"/> would show the bars before hiding them.
    /// </summary>
    public static TaskbarChoice Decide(bool hide, bool owns, bool marker, bool open)
    {
        if (hide)
            return owns ? default : new TaskbarChoice(Hide: true, Restore: false, Relayout: open);

        if (owns || marker)
            return new TaskbarChoice(Hide: false, Restore: true, Relayout: open);

        return default;
    }
}

internal interface ITaskbarSession
{
    bool Marker { get; }

    /// <summary>True when hide and restore change the work area the host should read again.</summary>
    bool ChangesWorkArea { get; }

    void Hide();

    void Restore();
}

/// <summary>
/// The taskbar session the host uses. <see cref="InstallSystem"/> switches it to the real bars. Until then the host
/// leaves them alone, so a test that reloads a config does not touch the desktop.
/// </summary>
public static class TaskbarControl
{
    internal static ITaskbarSession Current { get; set; } = NullTaskbarSession.Instance;

    public static void InstallSystem() => Current = LiveTaskbarSession.Instance;

    /// <summary>
    /// Shows the taskbar a Winland that was killed left hidden, from its crash marker. Uninstall runs this before it
    /// removes the folder the marker is in, so nothing is left that could put the taskbar back.
    /// </summary>
    public static void RecoverHidden()
    {
        if (Taskbar.MarkerPresent())
            Taskbar.Restore();
    }
}

internal sealed class NullTaskbarSession : ITaskbarSession
{
    internal static readonly NullTaskbarSession Instance = new();

    public bool Marker => false;

    public bool ChangesWorkArea => false;

    public void Hide()
    {
    }

    public void Restore()
    {
    }
}

internal sealed class LiveTaskbarSession : ITaskbarSession
{
    internal static readonly LiveTaskbarSession Instance = new();

    public bool Marker => Taskbar.MarkerPresent();

    public bool ChangesWorkArea => true;

    public void Hide() => Taskbar.Hide();

    public void Restore() => Taskbar.Restore();
}
