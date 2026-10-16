namespace Winland.Windows;

/// <summary>
/// The drag a mouse bind started, until its button comes up. Used only on the hook thread.
/// A drag that ends is handed back once, so the core is told of its end exactly once.
/// </summary>
internal sealed class MouseDrag
{
    internal readonly record struct Active(string Button, string Dispatcher, string Arguments);

    internal Active? Current { get; private set; }

    internal void Begin(string button, string dispatcher, string arguments) =>
        Current = new Active(button, dispatcher, arguments);

    /// <summary>Ends the drag when <paramref name="button"/> is the one that started it.</summary>
    internal Active? Release(string button)
    {
        if (Current is not Active active || !active.Button.Equals(button, StringComparison.OrdinalIgnoreCase))
            return null;

        Current = null;
        return active;
    }

    /// <summary>Ends the drag whatever its button, when the release can no longer be seen.</summary>
    internal Active? Abandon()
    {
        var active = Current;
        Current = null;
        return active;
    }
}
