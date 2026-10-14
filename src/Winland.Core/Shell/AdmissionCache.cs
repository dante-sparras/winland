namespace Winland.Core.Shell;

/// <summary>
/// The last Admission of every top-level window, shared by every caller until something could have changed it.
/// A reading is kept until a window is shown, hidden, destroyed, cloaked, or uncloaked (the generation moves),
/// until a window it read changes its style or title, or until it is <see cref="MaxAgeMs"/> old.
/// A new reading that finds the same windows hands out the lists of the one before, so an unchanged list keeps its reference.
/// Safe from any thread. The read itself runs outside the lock, so a thread reporting a change never waits on a census.
/// </summary>
public sealed class AdmissionCache
{
    /// <summary>The oldest reading that is still shared, in milliseconds. The safety net behind the events.</summary>
    public const long MaxAgeMs = 1000;

    private readonly Func<IReadOnlyList<WindowReading>> _read;
    private readonly Func<long> _clock;
    private readonly object _gate = new();
    private readonly Dictionary<long, (long Style, long ExtendedStyle)> _styles = [];

    // Every window a reading has seen, kept until it is destroyed. A reading leaves out a window that is hidden,
    // and the shell hides windows it still tracks: a group's other members and the scratchpad.
    private readonly HashSet<long> _seen = [];
    private const int MostSeen = 8192;

    // Every window destroyed while a census runs, kept until the last running census ends. The walk may have passed one before it went.
    private readonly HashSet<long> _departed = [];
    private int _reading;
    private AdmittedWindows? _admitted;

    // The lists last handed out, kept when the reading is dropped. A census that finds the same windows hands these out again.
    private AdmittedWindows? _last;
    private bool _everRead;
    private int _generation;
    private long _readAt;
    private int _version;

    /// <summary>Creates an empty cache.</summary>
    /// <param name="read">Reads every top-level window that may count, in enumeration order.</param>
    /// <param name="clock">Tick count in milliseconds.</param>
    public AdmissionCache(Func<IReadOnlyList<WindowReading>> read, Func<long> clock)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(clock);
        _read = read;
        _clock = clock;
    }

    /// <summary>The admitted windows, read again only when the shared reading is stale.</summary>
    /// <param name="generation">The window event generation now. A different one than the reading's makes it stale.</param>
    /// <returns>Tiles, picture-in-picture, and free windows.</returns>
    public AdmittedWindows Read(int generation)
    {
        int version;
        lock (_gate)
        {
            if (_admitted is { } admitted && _generation == generation && _clock() - _readAt < MaxAgeMs)
            {
                return admitted;
            }

            version = _version;
            _reading++;
        }

        IReadOnlyList<WindowReading> readings;
        AdmittedWindows fresh;
        try
        {
            readings = _read();
            fresh = WindowAdmission.Admit(readings);
        }
        catch
        {
            lock (_gate)
            {
                EndReading();
            }

            throw;
        }

        lock (_gate)
        {
            // A window destroyed after the walk passed it is in this census and off the glass. When this census is the first to see it,
            // its destroy did not count, so nothing asks for another census and the window would stay a tile.
            if (_departed.Count > 0 && Living(readings) is { } living)
            {
                readings = living;
                fresh = WindowAdmission.Admit(readings);
            }

            EndReading();
            fresh = Same(fresh);
            _everRead = true;

            // A change reported while the census ran may be missing from it. It is used once and not shared.
            if (_version == version)
            {
                _admitted = fresh;
                _generation = generation;
                _readAt = _clock();
                _styles.Clear();
                foreach (var reading in readings)
                {
                    _styles[reading.Id] = (reading.Style, reading.ExtendedStyle);
                }
            }

            if (_seen.Count > MostSeen)
            {
                _seen.Clear();
            }

            foreach (var reading in readings)
            {
                _seen.Add(reading.Id);
            }
        }

        return fresh;
    }

    /// <summary>
    /// A top-level window moved or resized. An app that goes fullscreen itself drops its caption without hiding,
    /// so a window the reading has seen with other styles makes the reading stale.
    /// </summary>
    /// <param name="id">The window.</param>
    /// <param name="style">Its style word now.</param>
    /// <param name="extendedStyle">Its extended style word now.</param>
    /// <returns>True when the reading was dropped.</returns>
    public bool NoteMoved(long id, long style, long extendedStyle)
    {
        lock (_gate)
        {
            if (!_styles.TryGetValue(id, out var seen) || (seen.Style == style && seen.ExtendedStyle == extendedStyle))
            {
                return false;
            }

            Drop();
            return true;
        }
    }

    /// <summary>A top-level window changed its title. A title marks a picture-in-picture player and labels a tile.</summary>
    /// <param name="id">The window.</param>
    /// <returns>True when the reading was dropped.</returns>
    public bool NoteRenamed(long id)
    {
        lock (_gate)
        {
            if (!_styles.ContainsKey(id))
            {
                return false;
            }

            Drop();
            return true;
        }
    }

    /// <summary>
    /// A window was destroyed. True when a reading has ever seen it, or when nothing has been read yet.
    /// A destroyed window can no longer say whether it was top-level, so this tells a window that counted from a control that went away.
    /// The windows a reading saw are kept when the reading is dropped, so a dropped reading does not make every destroyed control count.
    /// The window is forgotten, so its handle can be given to another.
    /// A census that is running leaves the window out, whether or not it counted.
    /// </summary>
    /// <param name="id">The window.</param>
    /// <returns>False only when something has been read and no reading ever saw <paramref name="id"/>.</returns>
    public bool Departed(long id)
    {
        lock (_gate)
        {
            if (_reading > 0)
            {
                _departed.Add(id);
            }

            return _seen.Remove(id) || !_everRead;
        }
    }

    /// <summary>One census ended. The destroyed windows are forgotten when no other census is running. Called under the lock.</summary>
    private void EndReading()
    {
        if (--_reading == 0)
        {
            _departed.Clear();
        }
    }

    /// <summary><paramref name="readings"/> without the windows destroyed while a census ran, or null when it holds none of them. Called under the lock.</summary>
    private List<WindowReading>? Living(IReadOnlyList<WindowReading> readings)
    {
        List<WindowReading>? living = null;
        for (var i = 0; i < readings.Count; i++)
        {
            if (_departed.Contains(readings[i].Id))
            {
                if (living is null)
                {
                    living = new List<WindowReading>(readings.Count);
                    for (var kept = 0; kept < i; kept++)
                    {
                        living.Add(readings[kept]);
                    }
                }
            }
            else
            {
                living?.Add(readings[i]);
            }
        }

        return living;
    }

    /// <summary>
    /// <paramref name="fresh"/> with each list that holds what the last reading's held replaced by that reading's list,
    /// so a caller can tell an unchanged list by its reference. A title counts, so no list is handed out with an old one. Called under the lock.
    /// </summary>
    private AdmittedWindows Same(AdmittedWindows fresh)
    {
        if (_last is { } last)
        {
            fresh = new AdmittedWindows(
                Same(last.Tiles, fresh.Tiles),
                Same(last.PictureInPicture, fresh.PictureInPicture),
                Same(last.Free, fresh.Free),
                Same(last.FreeAlone, fresh.FreeAlone));
        }

        _last = fresh;
        return fresh;
    }

    private static IReadOnlyList<T> Same<T>(IReadOnlyList<T> last, IReadOnlyList<T> fresh)
        where T : IEquatable<T>
    {
        if (last.Count != fresh.Count)
        {
            return fresh;
        }

        for (var i = 0; i < fresh.Count; i++)
        {
            if (!last[i].Equals(fresh[i]))
            {
                return fresh;
            }
        }

        return last;
    }

    /// <summary>
    /// True when the held reading admitted <paramref name="id"/> to any list, or when no reading is held to ask.
    /// A window that was admitted and has since changed its styles still counts when it is hidden.
    /// </summary>
    /// <param name="id">The window.</param>
    /// <returns>False only when a reading is held and it left <paramref name="id"/> out.</returns>
    public bool Admitted(long id)
    {
        lock (_gate)
        {
            if (_admitted is not { } admitted)
            {
                return true;
            }

            return Holds(admitted.Tiles, id)
                || admitted.PictureInPicture.Contains(id)
                || admitted.Free.Contains(id);
        }
    }

    private static bool Holds(IReadOnlyList<TrackedWindow> windows, long id)
    {
        for (var i = 0; i < windows.Count; i++)
        {
            if (windows[i].Id == id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Drops the reading, such as after Winoma changed a window's style itself.</summary>
    public void Forget()
    {
        lock (_gate)
        {
            Drop();
        }
    }

    private void Drop()
    {
        _admitted = null;
        _styles.Clear();
        _version++;
    }
}
