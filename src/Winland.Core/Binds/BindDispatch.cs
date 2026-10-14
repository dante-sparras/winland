namespace Winland.Core.Binds;

/// <summary>Delivers one key edge. A local bind runs in Winland. A global bind goes to the sink.</summary>
public static class BindDispatch
{
    /// <summary>
    /// Delivers <paramref name="chord"/>. A hold delivers both edges and returns true even when the edge has no command.
    /// A tap delivers the down edge only.
    /// </summary>
    /// <param name="table">The binds in force.</param>
    /// <param name="sink">Where a global bind goes.</param>
    /// <param name="local">Where a Winland command goes.</param>
    /// <param name="chord">The chord.</param>
    /// <param name="down">True when the key went down.</param>
    /// <returns>True when Winland owned the chord.</returns>
    public static bool Deliver(IBindTable table, IGlobalBindSink sink, IWinlandDispatch local, Chord chord, bool down)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(local);
        if (table.IsHold(chord))
        {
            if (table.ResolveHold(chord) is { } held)
            {
                Send(held, sink, local, down, hold: true);
            }

            return true;
        }

        if (!down)
        {
            return false;
        }

        if (table.Resolve(chord) is not { } command)
        {
            return false;
        }

        Send(command, sink, local, down: true, hold: false);
        return true;
    }

    private static void Send(Dispatch dispatch, IGlobalBindSink sink, IWinlandDispatch local, bool down, bool hold)
    {
        switch (dispatch)
        {
            case Dispatch.Local owned:
                local.Run(owned.Command);
                break;
            case Dispatch.Global global:
                sink.OnGlobal(global.Name, down, hold);
                break;
        }
    }
}
