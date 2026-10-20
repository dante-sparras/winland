using Windows.Win32;

namespace Winland.Windows;

/// <summary>
/// Keeps the wobble going after a window's motion ended: a thread that steps every lingering stand-in once a frame until each
/// one's tick says it is done. It runs only while a stand-in lingers.
/// </summary>
internal static class EffectPump
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<object, Func<bool>> Entries = [];
    private static Thread? _thread;
    private static bool _stopping;

    /// <summary>Starts calling <paramref name="tick"/> every frame until it returns false or <see cref="Remove"/> is called.</summary>
    internal static void Add(object key, Func<bool> tick)
    {
        lock (Gate)
        {
            if (_stopping)
                return;

            Entries[key] = tick;
            if (_thread is not null)
                return;

            _thread = new Thread(Run) { IsBackground = true, Name = "Winland effects" };
            _thread.Start();
        }
    }

    internal static void Remove(object key)
    {
        lock (Gate)
            _ = Entries.Remove(key);
    }

    /// <summary>Ends the thread and forgets every stand-in.</summary>
    internal static void Stop()
    {
        Thread? thread;
        lock (Gate)
        {
            _stopping = true;
            Entries.Clear();
            thread = _thread;
        }

        if (thread is not null && thread != Thread.CurrentThread)
            _ = thread.Join(TimeSpan.FromSeconds(2));

        lock (Gate)
        {
            _thread = null;
            _stopping = false;
        }
    }

    private static void Run()
    {
        while (true)
        {
            KeyValuePair<object, Func<bool>>[] entries;
            lock (Gate)
            {
                if (_stopping || Entries.Count == 0)
                {
                    if (!_stopping)
                        _thread = null;

                    return;
                }

                entries = [.. Entries];
            }

            foreach (var (key, tick) in entries)
            {
                bool more;
                try
                {
                    more = tick();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    Console.Error.WriteLine(exception.Message);
                    more = false;
                }

                if (!more)
                    Remove(key);
            }

            // DwmFlush returns after the next composition pass. Without composition it fails at once.
            if (PInvoke.DwmFlush().Failed)
                Thread.Sleep(15);
        }
    }
}
