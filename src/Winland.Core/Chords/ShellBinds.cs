namespace Winland.Core.Chords;

/// <summary>
/// A chord table read as binds. A window command is <see cref="Dispatch.Local"/>.
/// A plugin command is <see cref="Dispatch.Global"/> named <c>winshell:&lt;plugin&gt;:&lt;command&gt;</c>.
/// Every other command the table resolves is <c>winoma:</c> and the chord id it is stored as.
/// A hold that is not a plugin command stays a hold with nothing to deliver, which is what the shell did before.
/// </summary>
public sealed class ShellBinds : IBindTable
{
    private readonly IChordTable _chords;

    /// <summary>Reads <paramref name="chords"/>.</summary>
    /// <param name="chords">The shell's chord table.</param>
    public ShellBinds(IChordTable chords)
    {
        ArgumentNullException.ThrowIfNull(chords);
        _chords = chords;
    }

    /// <inheritdoc />
    public Dispatch? Resolve(Chord chord) => Of(_chords.Resolve(chord));

    /// <inheritdoc />
    public bool IsHold(Chord chord) => _chords.IsHold(chord);

    /// <inheritdoc />
    public Dispatch? ResolveHold(Chord chord) =>
        _chords.ResolveHold(chord) is ShellCommand.PluginCommand plugin ? Of(plugin) : null;

    private static Dispatch? Of(ShellCommand? command)
    {
        if (command is null)
        {
            return null;
        }

        if (WinlandShell.ToWinland(command) is { } owned)
        {
            return new Dispatch.Local(owned);
        }

        if (command is ShellCommand.PluginCommand plugin)
        {
            return new Dispatch.Global(GlobalName.Plugin(plugin.Plugin, plugin.Name));
        }

        var id = ChordCommands.IdOf(command);
        return id is null ? null : new Dispatch.Global(GlobalName.Winoma(id));
    }
}
