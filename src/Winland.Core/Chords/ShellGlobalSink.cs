namespace Winland.Core.Chords;

/// <summary>
/// Turns a global bind back into the shell command it was stored as and runs it.
/// <c>winshell:</c> is a plugin command. A tap is <see cref="BindPhase.Run"/> and a hold is the down and up edges.
/// <c>winoma:</c> is parsed as a chord id. A name the shell does not know, including <c>winoma:escape</c>, is ignored here.
/// </summary>
public sealed class ShellGlobalSink : IGlobalBindSink
{
    private readonly Action<ShellCommand> _run;

    /// <summary>Runs each command <paramref name="run"/> accepts.</summary>
    /// <param name="run">Where a recognized global goes. The shell passes the method that runs a chord.</param>
    public ShellGlobalSink(Action<ShellCommand> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        _run = run;
    }

    /// <inheritdoc />
    public void OnGlobal(string name, bool down, bool hold)
    {
        if (GlobalName.TryPlugin(name, out var plugin, out var command))
        {
            var phase = hold
                ? down ? BindPhase.Down : BindPhase.Up
                : BindPhase.Run;
            _run(new ShellCommand.PluginCommand(plugin, command, phase));
            return;
        }

        if (!down || !GlobalName.TryWinoma(name, out var id))
        {
            return;
        }

        if (ChordCommands.TryParse(id, out var parsed) && parsed is not null)
        {
            _run(parsed);
        }
    }
}
