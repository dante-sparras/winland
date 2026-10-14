namespace Winland.Core.Chords;

/// <summary>
/// Chords for an administrator window the desktop hook cannot see.
/// The helper owns a key only while that window is the Windows foreground, and then only a chord the table resolves.
/// It closes that window itself and hands every other chord to the desktop. An unbound Super chord is replayed, so Windows still sees it.
/// A key the desktop hook did see is passed through.
/// </summary>
public sealed class ElevatedChordGate
{
    private readonly IChordTable _chords;
    private readonly ChordLedger _ledger;
    private bool _owning;
    private DesktopSuper? _desktopSuperGone;

    /// <summary>Uses <paramref name="chords"/> as the whole map.</summary>
    /// <param name="chords">The chord file, or the built-in map when that file is missing.</param>
    public ElevatedChordGate(IChordTable chords)
    {
        ArgumentNullException.ThrowIfNull(chords);
        _chords = chords;
        _ledger = new ChordLedger(new ShellBinds(chords));
    }

    /// <summary>One physical key.</summary>
    /// <param name="key">The key.</param>
    /// <param name="down">True for key-down, including auto-repeat.</param>
    /// <param name="now">Tick count.</param>
    /// <param name="desktopSawThis">True when the desktop hook already published this same key event.</param>
    /// <param name="own">True when an administrator window is the Windows foreground. The desktop hook is not called then.</param>
    /// <param name="desktopSuper">
    /// The Super finger the desktop hook holds, or null. A Super pressed before an administrator window took the foreground,
    /// such as the Super of a workspace chord that landed on one, still makes the next key a chord.
    /// Its key-up here never reaches the desktop hook, so <see cref="DesktopSuperWentUp"/> reports it.
    /// </param>
    /// <returns>What the elevated hook should do after it returns.</returns>
    public GateEffect OnKey(PhysicalKey key, bool down, long now, bool desktopSawThis, bool own, DesktopSuper? desktopSuper = null)
    {
        DesktopSuperWentUp = false;
        if (desktopSuper is null)
        {
            _desktopSuperGone = null;
        }

        if (!own || desktopSawThis)
        {
            Release();
            return GateEffect.Pass;
        }

        _owning = true;
        if (desktopSuper is DesktopSuper held && held != _desktopSuperGone)
        {
            if (key.Role == KeyRole.Super && key.Id == held.Key && !down)
            {
                _desktopSuperGone = held;
                DesktopSuperWentUp = true;
            }
            else if (key.Role != KeyRole.Super && !_ledger.HoldingSuper)
            {
                _ = _ledger.AdoptSuper(PhysicalKey.Super(held.Key), now);
            }
        }

        return _ledger.OnKey(key, down, now);
    }

    /// <summary>True when the last <see cref="OnKey"/> was the key-up of the desktop hook's Super. Tell the desktop it went up.</summary>
    public bool DesktopSuperWentUp { get; private set; }

    /// <summary>True when <paramref name="effect"/> is the press of a chord <see cref="IChordTable.Resolve"/> maps to close.</summary>
    /// <param name="effect">The result of <see cref="OnKey"/>.</param>
    /// <returns>True when the foreground window should be closed.</returns>
    public bool Closes(GateEffect effect) =>
        effect.Queued is { Down: true, Chord: var chord }
        && _chords.Resolve(chord) is ShellCommand.Window { Action: WindowAction.Close };

    /// <summary>The chord the desktop should run for <paramref name="effect"/>, or null. A close press is the helper's own.</summary>
    /// <param name="effect">The result of <see cref="OnKey"/>.</param>
    /// <returns>The chord to send to the desktop.</returns>
    public QueuedChord? Forwards(GateEffect effect) =>
        effect.Queued is QueuedChord queued && !Closes(effect) ? queued : null;

    private void Release()
    {
        if (!_owning)
        {
            return;
        }

        _owning = false;
        _ledger.Abandon();
    }
}
