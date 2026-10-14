namespace Winland.Core.Chords;

/// <summary>Collapses a burst of workspace jumps so only the last desktop change runs.</summary>
public static class ChordBatch
{
    /// <summary>
    /// Drops earlier jumps when several land before the desktop can catch up.
    /// Key-ups do not break that run, and a non-jump down does. Jump key-ups in the run are dropped.
    /// Every other event stays, in its order around the last jump.
    /// </summary>
    public static IReadOnlyList<(Chord Chord, bool Down)> Collapse(IReadOnlyList<(Chord Chord, bool Down)> keys, IChordTable? chords = null)
    {
        var map = chords ?? ChordMap.Shared;
        var result = new List<(Chord Chord, bool Down)>(keys.Count);
        for (var i = 0; i < keys.Count; i++)
        {
            if (!keys[i].Down || map.Resolve(keys[i].Chord) is not ShellCommand.JumpWorkspace)
            {
                result.Add(keys[i]);
                continue;
            }

            var last = keys[i];
            var end = i;
            var before = new List<(Chord Chord, bool Down)>();
            var after = new List<(Chord Chord, bool Down)>();
            for (var j = i + 1; j < keys.Count; j++)
            {
                if (!keys[j].Down)
                {
                    if (map.Resolve(keys[j].Chord) is not ShellCommand.JumpWorkspace)
                    {
                        after.Add(keys[j]);
                    }

                    end = j;
                    continue;
                }

                if (map.Resolve(keys[j].Chord) is not ShellCommand.JumpWorkspace)
                {
                    break;
                }

                before.AddRange(after);
                after.Clear();
                last = keys[j];
                end = j;
            }

            result.AddRange(before);
            result.Add(last);
            result.AddRange(after);
            i = end;
        }

        return result;
    }
}
